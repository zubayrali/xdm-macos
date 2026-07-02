// Detects embedded videos (YouTube / Vimeo iframes + plyr players) on a page —
// e.g. LMS lessons (Tutor LMS, Moodle) that wrap a YouTube embed. Normalizes each
// to a canonical watch URL yt-dlp can resolve, and hands them to the service worker,
// which POSTs them to XDM's /ydl endpoint (-> yt-dlp format picker).
//
// Runs both as an auto content script (all pages) and via executeScript from the
// "Grab embedded video" context-menu action. Safe to run multiple times.
(() => {
    const urls = new Set();

    const ytWatch = (id) => "https://www.youtube.com/watch?v=" + id;
    const vimeo = (id) => "https://vimeo.com/" + id;

    // 1. <iframe> embeds
    for (const f of document.querySelectorAll("iframe[src]")) {
        const src = f.src || "";
        let m;
        if ((m = src.match(/(?:youtube(?:-nocookie)?\.com)\/embed\/([\w-]{6,})/))) {
            urls.add(ytWatch(m[1]));
        } else if ((m = src.match(/player\.vimeo\.com\/video\/(\d+)/))) {
            urls.add(vimeo(m[1]));
        }
    }

    // 2. plyr players (Tutor LMS uses these) — the id lives in a data attribute,
    //    the <iframe> may not exist until the user hits play.
    for (const p of document.querySelectorAll("[data-plyr-embed-id]")) {
        const id = p.getAttribute("data-plyr-embed-id");
        const provider = (p.getAttribute("data-plyr-provider") || "").toLowerCase();
        if (!id) continue;
        if (provider === "vimeo" || /^\d+$/.test(id)) urls.add(vimeo(id));
        else urls.add(ytWatch(id));
    }

    // 3. og:video / twitter:player meta (some LMS themes expose the embed here)
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

    if (urls.size > 0) {
        chrome.runtime.sendMessage({ type: "embeds", embeds: Array.from(urls) });
    }
})();
