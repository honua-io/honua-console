import {readFileSync} from 'node:fs';

export function readConversationMarkup() {
    const directory = new URL('../../src/Honua.Console.Shell/Layout/', import.meta.url);
    return readFileSync(new URL('ConsoleConversationDrawer.razor', directory), 'utf8')
        .replace(/<ConsoleConversation(\w+) \/>/g, (_, name) =>
            readFileSync(new URL(`ConsoleConversation${name}.razor`, directory), 'utf8'));
}
