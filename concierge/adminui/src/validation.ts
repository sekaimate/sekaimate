/** Validate the explicit browser transport pair accepted by concierge. */
export const validateBrowserEndpoints = (webSocketUri: string, serverInfoUri: string): string | null => {
  const ws = webSocketUri.trim();
  const info = serverInfoUri.trim();
  if (!ws && !info) return null;
  if (!ws || !info) return "WebSocket URI と Server Info URI は両方入力するか、両方空欄にしてください。";
  try {
    const wsURL = new URL(ws);
    const infoURL = new URL(info);
    if (wsURL.protocol !== "ws:" && wsURL.protocol !== "wss:") return "WebSocket URI は ws:// または wss:// で入力してください。";
    if (infoURL.protocol !== "http:" && infoURL.protocol !== "https:") return "Server Info URI は http:// または https:// で入力してください。";
    const loopback = ["localhost", "127.0.0.1", "[::1]", "::1"].includes(wsURL.hostname);
    if (!loopback && wsURL.protocol !== "wss:") return "リモートの WebSocket URI には wss:// が必要です。";
    const infoLoopback = ["localhost", "127.0.0.1", "[::1]", "::1"].includes(infoURL.hostname);
    if (!infoLoopback && infoURL.protocol !== "https:") return "リモートの Server Info URI には https:// が必要です。";
  } catch {
    return "WebGL 接続先 URI の形式が正しくありません。";
  }
  return null;
};
