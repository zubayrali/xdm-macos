"use strict";
import Logger from './logger.js';
import RequestWatcher from './request-watcher.js';
import Connector from './connector.js';

export default class App {
    constructor() {
        this.logger = new Logger();
        this.videoList = [];
        this.blockedHosts = [];
        this.fileExts = [];
        this.requestWatcher = new RequestWatcher(this.onRequestDataReceived.bind(this));
        this.tabsWatcher = [];
        this.userDisabled = false;
        this.appEnabled = false;
        this.onDownloadCreatedCallback = this.onDownloadCreated.bind(this);
        this.onDeterminingFilenameCallback = this.onDeterminingFilename.bind(this);
        this.onTabUpdateCallback = this.onTabUpdate.bind(this);
        this.activeTabId = -1;
        this.connector = new Connector(this.onMessage.bind(this), this.onDisconnect.bind(this));
    }

    start() {
        this.logger.log("starting...");
        // Restore the user's monitoring on/off choice (survives service-worker restarts)
        try {
            chrome.storage.local.get(["userDisabled"], data => {
                this.userDisabled = data && data.userDisabled === true;
                this.updateActionIcon();
            });
        } catch (e) { this.logger.log(e); }
        this.starAppConnector();
        this.register();
        this.logger.log("started.");
    }

    persistDisabled() {
        try { chrome.storage.local.set({ userDisabled: this.userDisabled }); }
        catch (e) { this.logger.log(e); }
    }

    starAppConnector() {
        this.connector.connect();
    }

    onMessage(msg) {
        this.logger.log("message from XDM");
        this.logger.log(msg);
        this.appEnabled = msg.enabled === true;
        this.fileExts = msg.fileExts;
        this.blockedHosts = msg.blockedHosts;
        this.tabsWatcher = msg.tabsWatcher;
        this.videoList = msg.videoList;
        this.requestWatcher.updateConfig({
            mediaExts: msg.requestFileExts,
            blockedHosts: msg.blockedHosts,
            matchingHosts: msg.matchingHosts,
            mediaTypes: msg.mediaTypes
        });
        this.updateActionIcon();
    }

    onDisconnect() {
        this.logger.log("Disconnected from native host!");
        this.logger.log("Disconnected...");
        this.updateActionIcon();
    }

    isMonitoringEnabled() {
        this.logger.log(this.appEnabled + " " + this.userDisabled);
        return this.appEnabled === true && this.userDisabled === false && this.connector.isConnected();
    }

    onRequestDataReceived(data) {
        //Streaming video data received, send to native messaging application
        this.logger.log("onRequestDataReceived");
        this.logger.log(data);
        this.isMonitoringEnabled() && this.connector.isConnected() && this.connector.postMessage("/media", data);
    }

    onDeterminingFilename(download, suggest) {
        this.logger.log("onDeterminingFilename");
        if (!this.isMonitoringEnabled()) {
            return;
        }
        this.logger.log(download);
        let url = download.finalUrl || download.url;
        this.logger.log(url);
        if (this.isMonitoringEnabled() && this.shouldTakeOver(url, download.filename)) {
            chrome.downloads.cancel(
                download.id,
                () => chrome.downloads.erase({ id: download.id })
            );
            let referrer = download.referrer;
            if (!referrer && download.finalUrl !== download.url) {
                referrer = download.url;
            }
            this.triggerDownload(url, download.filename,
                referrer, download.fileSize, download.mime);
        }
    }

    onDownloadCreated(download) {
        this.logger.log("onDownloadCreated");
        this.logger.log(download);
    }

    onTabUpdate(tabId, changeInfo, tab) {
        if (!this.isMonitoringEnabled()) {
            return;
        }
        if (changeInfo.title) {
            // Forward the page title for ALL sites, not just youtube. Many sites set
            // the real <title> after the video manifest has already loaded, so this
            // lets XDM rename a captured video once the proper title appears.
            this.logger.log("Tab changed: " + changeInfo.title + " => " + tab.url);
            try {
                this.connector.postMessage("/tab-update", {
                    tabUrl: tab.url,
                    tabTitle: changeInfo.title
                });
            } catch (ex) {
                console.log(ex);
            }
        }
    }

    register() {
        chrome.downloads.onCreated.addListener(
            this.onDownloadCreatedCallback
        );
        chrome.downloads.onDeterminingFilename.addListener(
            this.onDeterminingFilenameCallback
        );
        chrome.tabs.onUpdated.addListener(
            this.onTabUpdateCallback
        );
        chrome.runtime.onMessage.addListener(this.onPopupMessage.bind(this));
        this.requestWatcher.register();
        this.attachContextMenu();
        chrome.tabs.onActivated.addListener(this.onTabActivated.bind(this));
        // Keyboard shortcut (Cmd/Ctrl+Shift+E) to toggle monitoring on/off
        if (chrome.commands && chrome.commands.onCommand) {
            chrome.commands.onCommand.addListener(command => {
                if (command !== "toggle-monitoring") return;
                this.userDisabled = !this.userDisabled;
                this.persistDisabled();
                this.updateActionIcon();
                this.logger.log("toggle-monitoring -> userDisabled=" + this.userDisabled);
            });
        }
    }

    isSupportedProtocol(url) {
        if (!url) return false;
        let u = new URL(url);
        return u.protocol === 'http:' || u.protocol === 'https:';
    }

    shouldTakeOver(url, file) {
        let u = new URL(url);
        if (!this.isSupportedProtocol(url)) {
            return false;
        }
        let hostName = u.host;
        if (this.blockedHosts.find(item => hostName.indexOf(item) >= 0)) {
            return false;
        }
        let path = file || u.pathname;
        let upath = path.toUpperCase();
        if (this.fileExts.find(ext => upath.endsWith(ext))) {
            return true;
        }
        return false;
    }

    updateActionIcon() {
        chrome.action.setIcon({ path: this.getActionIcon() });
        let vc = "";
        if (this.videoList && this.videoList.length > 0) {
            let len = this.videoList.length;
            if (len > 0) {
                vc = len + "";
            }
        }
        // if (this.videoList && this.videoList.length > 0) {
        //     let len = this.videoList.filter(vid => {
        //         if (!vid.tabId) {
        //             return true;
        //         }
        //         if (vid.tabId == '-1') {
        //             return true;
        //         }
        //         return (vid.tabId == this.activeTabId);
        //     }).length;
        //     if (len > 0) {
        //         vc = len + "";
        //     }
        // }
        chrome.action.setBadgeText({ text: vc });
        if (!this.connector.isConnected()) {
            this.logger.log("Not connected...");
            chrome.action.setPopup({ popup: "./error.html" });
            return;
        }
        if (!this.appEnabled) {
            chrome.action.setPopup({ popup: "./disabled.html" });
            return;
        }
        else {
            chrome.action.setPopup({ popup: "./popup.html" });
            return;
            // if (this.videoList && this.videoList.length > 0) {
            //     chrome.action.setBadgeText({ text: this.videoList.length + "" });
            // }
        }
    }

    getActionIconName(icon) {
        return this.isMonitoringEnabled() ? icon + ".png" : icon + "-mono.png";
    }

    getActionIcon() {
        return {
            "16": this.getActionIconName("icon16"),
            "48": this.getActionIconName("icon48"),
            "128": this.getActionIconName("icon128")
        }
    }

    triggerDownload(url, file, referer, size, mime) {
        chrome.cookies.getAll({ "url": url }, cookies => {
            let cookieStr = undefined;
            if (cookies) {
                cookieStr = cookies.map(cookie => cookie.name + "=" + cookie.value).join("; ");
            }
            let requestHeaders = { "User-Agent": [navigator.userAgent] };
            if (referer) {
                requestHeaders["Referer"] = [referer];
            }
            let responseHeaders = {};
            if (size) {
                let fz = +size;
                if (fz > 0) {
                    responseHeaders["Content-Length"] = [fz];
                }
            }
            if (mime) {
                responseHeaders["Content-Type"] = [mime];
            }
            let data = {
                url: url,
                cookie: cookieStr,
                requestHeaders: requestHeaders,
                responseHeaders: responseHeaders,
                filename: file,
                fileSize: size,
                mimeType: mime
            };
            this.logger.log(data);
            this.connector.postMessage("/download", data);
        });
    }

    diconnect() {
        this.onDisconnect();
    }

    onPopupMessage(request, sender, sendResponse) {
        this.logger.log(request.type);
        if (request.type === "stat") {
            let resp = {
                enabled: this.isMonitoringEnabled(),
                list: this.videoList
                // list: this.videoList.filter(vid => {
                //     if (!vid.tabId) {
                //         return true;
                //     }
                //     return (vid.tabId == this.activeTabId);
                // })
            };
            sendResponse(resp);
        }
        else if (request.type === "cmd") {
            this.userDisabled = request.enabled === false;
            this.persistDisabled();
            this.logger.log("request.enabled:" + request.enabled);
            if (request.enabled && !this.connector.isConnected()) {
                this.connector.launchApp();
                return;
            }
            this.updateActionIcon();
        }
        else if (request.type === "links") {
            this.sendBatchLinks(request.links);
        }
        else if (request.type === "vid") {
            let vid = request.itemId;
            this.connector.postMessage("/vid", {
                vid: vid + "",
            });
        }
        else if (request.type === "clear") {
            this.connector.postMessage("/clear", {});
        }
    }

    sendLinkToXDM(info, tab) {
        let url = info.linkUrl;
        if (!this.isSupportedProtocol(url)) {
            url = info.srcUrl;
        }
        if (!this.isSupportedProtocol(url)) {
            url = info.pageUrl;
        }
        if (!this.isSupportedProtocol(url)) {
            return;
        }
        this.triggerDownload(url, null, info.pageUrl, null, null);
    }

    sendImageToXDM(info, tab) {
        let url = info.srcUrl;
        if (!this.isSupportedProtocol(url))
            url = info.linkUrl;
        if (!this.isSupportedProtocol(url)) {
            url = info.pageUrl;
        }
        if (!this.isSupportedProtocol(url)) {
            return;
        }
        this.triggerDownload(url, null, info.pageUrl, null, null);
    }

    onMenuClicked(info, tab) {
        if (info.menuItemId == "download-any-link") {
            this.sendLinkToXDM(info, tab);
        }
        if (info.menuItemId == "download-image-link") {
            this.sendImageToXDM(info, tab);
        }
        if (info.menuItemId == "download-all-links") {
            this.downloadAllLinks(tab);
        }
    }

    downloadAllLinks(tab) {
        if (!tab || tab.id == null) return;
        // Inject a content script that gathers every link on the page; it posts them
        // back as a {type:"links"} message which we forward to XDM as a batch.
        chrome.scripting.executeScript({
            target: { tabId: tab.id },
            files: ['contentscript.js']
        }).catch(e => this.logger.log(e));
    }

    // Forward a list of URLs to XDM's batch endpoint, which opens a single selection
    // window listing them all (better UX than one dialog per link).
    sendBatchLinks(links) {
        if (!Array.isArray(links) || links.length === 0) return;
        if (!this.isMonitoringEnabled() || !this.connector.isConnected()) return;
        const seen = new Set();
        const batch = [];
        for (const url of links) {
            if (!url || !this.isSupportedProtocol(url)) continue;
            const u = (url + "").trim();
            if (!u || seen.has(u)) continue;
            seen.add(u);
            batch.push({
                url: u,
                method: "GET",
                requestHeaders: { "User-Agent": [navigator.userAgent] },
                responseHeaders: {},
                cookie: undefined
            });
            if (batch.length >= 500) break;
        }
        if (batch.length > 0) {
            this.connector.postMessage("/link", batch);
        }
    }

    attachContextMenu() {
        // removeAll first so re-registration on service-worker wake doesn't throw
        // "duplicate id" errors (the menus are otherwise recreated every restart).
        chrome.contextMenus.removeAll(() => {
            chrome.contextMenus.create({
                id: 'download-any-link',
                title: "Download with XDM",
                contexts: ["link", "video", "audio"]
            });

            chrome.contextMenus.create({
                id: 'download-image-link',
                title: "Download Image with XDM",
                contexts: ["image"]
            });

            chrome.contextMenus.create({
                id: 'download-all-links',
                title: "Download all links",
                contexts: ["all"]
            });
        });

        chrome.contextMenus.onClicked.addListener(this.onMenuClicked.bind(this));
    }

    onTabActivated(activeInfo) {
        this.activeTabId = activeInfo.tabId + "";
        this.logger.log("Active tab: " + this.activeTabId);
        this.updateActionIcon();
    }
}
