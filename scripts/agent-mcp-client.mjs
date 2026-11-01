/** Minimal deterministic client for the real server MCP endpoint used by live journeys. */
export class AgentMcpClient {
  #url; #key; #session; #id = 0;
  constructor(baseUrl, apiKey) {
    this.#url = new URL('/mcp', baseUrl);
    if (!apiKey) throw new Error('MCP actor credential is required');
    this.#key = apiKey;
  }
  async #rpc(method, params) {
    const id = ++this.#id;
    const headers = { 'X-API-Key': this.#key, 'Accept': 'application/json, text/event-stream', 'Content-Type': 'application/json' };
    if (this.#session) headers['Mcp-Session-Id'] = this.#session;
    const response = await fetch(this.#url, { method: 'POST', headers,
      body: JSON.stringify({ jsonrpc: '2.0', id, method, params }), signal: AbortSignal.timeout(30_000) });
    if (!response.ok) throw new Error(`MCP HTTP ${response.status} for ${method}`);
    const session = response.headers.get('mcp-session-id');
    if (session) this.#session = session;
    let body = await response.text();
    if (response.headers.get('content-type')?.includes('text/event-stream'))
      body = body.split(/\r?\n/).filter(line => line.startsWith('data:')).map(line => line.slice(5).trim()).join('\n');
    const envelope = JSON.parse(body);
    if (envelope.id !== id || envelope.error || !envelope.result) throw new Error(`MCP ${method} failed`);
    return envelope.result;
  }
  async initialize() {
    await this.#rpc('initialize', { protocolVersion: '2025-03-26', capabilities: {}, clientInfo: { name: 'console-customer-journey', version: '1' } });
    if (!this.#session) throw new Error('MCP did not establish a session');
  }
  async call(name, arguments_) {
    if (!this.#session) throw new Error('MCP session is not initialized');
    const result = await this.#rpc('tools/call', { name, arguments: arguments_ });
    if (result.isError || !result.structuredContent) throw new Error(`MCP tool ${name} failed or returned no structured output`);
    return result.structuredContent;
  }
}
