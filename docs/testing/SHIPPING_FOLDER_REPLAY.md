# Shipping folder navigation replay

Run the packaged quickstart with `e2e/run-shipping-live.sh` as described in
[the shipping password replay](SHIPPING_LIVE_REPLAY.md). The shipping suite
includes `folder-navigation.spec.ts` at 1280 and 390 pixel widths, with no route
interception or development web host.

The disposable database fixture adds `ShippingFolder` and one service over the
same single-point table used by the endpoint check. The selected server's
compatibility compiler projects its layer into vector and imagery resources,
so the folder reports two layers. Navigation targets the vector resource. The
root endpoint service stays outside the folder. The compiler creates the
metadata snapshot. The fixture also registers the quickstart database using its
existing `env:ConnectionStrings__DefaultConnection` secret reference, so the
connection-based navigation tree can discover the layer without copied credentials.
`shipping-folder.sql` adds membership and refreshes
the snapshot and current-pointer ETags before the server restarts. This setup
does not exercise folder creation, import, publication approval, or mutation APIs.

The replay independently reads folder membership from the real admin API using
the browser session, follows the folder's mounted service link, opens the layer
through the service navigation tree, and checks its heading and mounted
Home/Folder/Service breadcrumbs before and after reload. Screenshots of the
folder, service, layer, and reloaded layer are attached per viewport. A separate
anonymous request context must receive 401 from both folder catalog reads.

Test discovery and fixture generation alone are not a live pass. Verification
must record the selected immutable images, Console bundle checksum, successful
browser results, and screenshots from the packaged stack. The older
`live/specs/folder-tree.live.spec.ts` remains a separate import journey against
the development host; this replay qualifies navigation over preloaded data.
