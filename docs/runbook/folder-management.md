---
type: guide
---

# Manage Folders

Open **Operate → Services → Folders**, or **Manage Folders** in the Service navigation.
Root contains Services with no Folder. Empty Folders appear in the same catalog.
Create a Folder with a single path segment and optional display title. Select an
existing Folder to rename its path or change its title; clearing the title removes
the display override. A title change leaves public paths unchanged.

On a Service's settings page, choose its destination Folder or Root and select
**Move Service**. Logical Service identity remains unchanged. Read the refreshed
Service endpoints after the move. Previous public paths remain aliases for saved
client connections. Access is managed per Service; Folders introduce no separate
access policy.

Only empty Folders can be deleted. Select the Folder, confirm deletion and choose
**Delete empty Folder**. The server explains conflicts and non-empty refusals.

Publishing keeps Folder membership, public Service names and logical identities
separate. The Folder picker distinguishes preserving membership from explicitly
selecting Root. New Services default to Root when membership is omitted. A public
Service name is immutable after assignment. The publishing wizard groups existing
Services by their current Folder and reads Service endpoints after publishing.

Folder edits use the catalog ETag and the human/environment authority captured
by the read. A changed catalog, expired sign-in or changed authority requires a
refresh and another deliberate action. Edits remain visible after a refusal;
refresh never resubmits them. An unconfirmed response requires checking the
original environment before another write. After an unconfirmed publish, select
**Read original catalog** while signed in to that environment. The retained
readback names the requested Service and Layer and shows the matching Service's
layer count, or explains that the Service was not found on the first page.
Inspect the requested Layer in **Open Service settings** before trying again;
reading never resubmits the publish. Server-required approvals remain in
the separate human approval channel, available from **Open approvals**.

A connected agent workspace cannot retain Folder or public-name publishing
choices yet. Its existing publish action refuses those choices. Use the publishing
wizard for them. This does not change the server's approval policy.

The live `folder-management.live.spec.ts` journey uses the governed human session,
a real server and PostgreSQL to create an empty Folder, publish into it, rename,
move to Root, delete, follow a saved alias and check stale and denied writes.
The Folder tree's read journey remains separately covered by the folder browsing
suite. Run against an immutable server image containing the Folder requester-policy
safeguard; older images cannot qualify this feature.
