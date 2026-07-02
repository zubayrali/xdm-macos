// Injected on "Download all links" to collect every link (and media source) on the
// page and hand them back to the service worker, which forwards them to XDM as a batch.
(() => {
    const urls = new Set();
    for (const a of document.links || []) {
        if (a && a.href) urls.add(a.href);
    }
    for (const el of document.querySelectorAll("video[src], audio[src], source[src]")) {
        if (el.src) urls.add(el.src);
    }
    chrome.runtime.sendMessage({ type: "links", links: Array.from(urls) });
})();
