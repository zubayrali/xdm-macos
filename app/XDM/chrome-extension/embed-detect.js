// Detects embedded videos (YouTube / Vimeo iframes + plyr players) on a page —
// e.g. LMS lessons (Tutor LMS, Moodle) that wrap a YouTube embed. Normalizes each
// to a canonical watch URL yt-dlp can resolve, and hands them to the service worker,
// which POSTs them to XDM's /ydl endpoint (-> yt-dlp format picker).
//
// LMS/Elementor pages build the player with JS *after* load, so a single scan at
// document_idle finds nothing. We scan on load, on DOM mutations, and on a few timed
// retries, sending each new URL once. Runs in all frames (embed may be in a sub-frame).
(() => {
    // guard against double-injection (manifest auto + context-menu re-inject).
    // If already active, treat re-injection as a forced rescan (menu action).
    if (window.__xdmEmbedDetect) {
        if (window.__xdmRescan) window.__xdmRescan();
        return;
    }
    window.__xdmEmbedDetect = true;

    const log = (...a) => { try { console.log("[XDM-embed]", ...a); } catch (e) { } };
    const sent = new Set();

    const ytWatch = (id) => "https://www.youtube.com/watch?v=" + id;
    // Domain-restricted Vimeo embeds 404 on vimeo.com/ID and need the player URL,
    // often with the hosting page as Referer. Carry the page origin in a fragment;
    // YDLProcess strips it and passes it to yt-dlp as --referer.
    const vimeo = (id) =>
        "https://player.vimeo.com/video/" + id + "#__xdmref=" + encodeURIComponent(location.origin);

    function scan() {
        const urls = new Set();

        // 1. <iframe> embeds
        for (const f of document.querySelectorAll("iframe[src], iframe[data-src]")) {
            const src = f.src || f.getAttribute("data-src") || "";
            let m;
            if ((m = src.match(/(?:youtube(?:-nocookie)?\.com)\/embed\/([\w-]{6,})/))) {
                urls.add(ytWatch(m[1]));
            } else if ((m = src.match(/player\.vimeo\.com\/video\/(\d+)/))) {
                urls.add(vimeo(m[1]));
            }
        }

        // 2. plyr players (Tutor LMS / Elementor use these). The id lives in a data
        //    attribute and is present before the <iframe> is built.
        for (const p of document.querySelectorAll("[data-plyr-embed-id]")) {
            const id = p.getAttribute("data-plyr-embed-id");
            const provider = (p.getAttribute("data-plyr-provider") || "").toLowerCase();
            if (!id) continue;
            if (provider === "vimeo" || /^\d+$/.test(id)) urls.add(vimeo(id));
            else urls.add(ytWatch(id));
        }

        // 3. og:video / twitter:player meta
        for (const meta of document.querySelectorAll(
            'meta[property="og:video"], meta[property="og:video:url"], meta[name="twitter:player"]')) {
            const c = meta.getAttribute("content") || "";
            let m;
            if ((m = c.match(/(?:youtube(?:-nocookie)?\.com)\/(?:embed|watch\?v=)\/?([\w-]{6,})/))) {
                urls.add(ytWatch(m[1]));
            } else if ((m = c.match(/player\.vimeo\.com\/video\/(\d+)/))) {
                urls.add(vimeo(m[1]));
            }
        }

        return [...urls];
    }

    function report() {
        const fresh = scan().filter((u) => !sent.has(u));
        if (fresh.length === 0) return;
        fresh.forEach((u) => sent.add(u));
        log("found embeds:", fresh);
        try {
            chrome.runtime.sendMessage({ type: "embeds", embeds: fresh });
        } catch (e) {
            log("sendMessage failed (service worker asleep?)", e);
        }
    }

    // forced rescan (context-menu "Grab embedded video") re-sends even if already seen
    window.__xdmRescan = () => { sent.clear(); log("forced rescan"); report(); };

    log("scanner active on", location.href);
    report();

    // Catch embeds injected after initial load (plyr/Elementor/AJAX lessons).
    let ticks = 0;
    const timer = setInterval(() => {
        report();
        if (++ticks >= 8) clearInterval(timer); // ~12s of coverage
    }, 1500);

    const obs = new MutationObserver(() => report());
    obs.observe(document.documentElement, { childList: true, subtree: true });
    setTimeout(() => obs.disconnect(), 15000);
})();
