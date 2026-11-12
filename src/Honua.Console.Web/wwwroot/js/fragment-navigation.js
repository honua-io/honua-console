// In-page section navigation: Blazor's interactive router intercepts same-page
// "#id" links and does not scroll to the fragment, so handle pure-fragment anchors
// here. Only acts when a matching element exists; otherwise leaves the link alone.
// Capture phase so this runs before Blazor's delegated router handler.
document.addEventListener('click', function (e) {
    var anchor = e.target.closest && e.target.closest('a[href^="#"]');
    if (!anchor) { return; }
    var id = anchor.getAttribute('href').slice(1);
    if (!id) { return; }
    var target = document.getElementById(id);
    if (target) {
        e.preventDefault();
        e.stopPropagation();
        target.scrollIntoView({ behavior: 'smooth', block: 'start' });
        if (history.replaceState) { history.replaceState(null, '', '#' + id); }
    }
}, true);
