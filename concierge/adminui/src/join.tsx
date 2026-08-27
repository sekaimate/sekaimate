import { useEffect, useState } from "react";
import { createRoot } from "react-dom/client";
import "@cloudscape-design/global-styles/index.css";
import Box from "@cloudscape-design/components/box";
import Button from "@cloudscape-design/components/button";
import Container from "@cloudscape-design/components/container";
import Header from "@cloudscape-design/components/header";
import SpaceBetween from "@cloudscape-design/components/space-between";
import "./join.css";

type MeetingDetails = {
  meeting?: { id?: string; title?: string };
  connection?: {
    host?: string;
    port?: number;
    password?: string;
    webSocketUri?: string;
    serverInfoUri?: string;
  };
};

function JoinPage() {
  const [details, setDetails] = useState<MeetingDetails | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [status, setStatus] = useState("参加方法を選択してください。");

  const token = decodeURIComponent(window.location.pathname.split("/").filter(Boolean).pop() ?? "");

  useEffect(() => {
    if (!token) {
      setError("参加リンクが正しくありません。");
      return;
    }
    fetch(`/join/${encodeURIComponent(token)}/manifest`, { cache: "no-store" })
      .then(async (response) => {
        if (!response.ok) throw new Error(await response.text());
        return response.json() as Promise<MeetingDetails>;
      })
      .then(setDetails)
      .catch(() => setError("この参加リンクは無効か、会議の準備が完了していません。"));
  }, [token]);

  useEffect(() => {
    const onMessage = (event: MessageEvent) => {
      if (event.data === "basis-join-received") setStatus("Basisに会議への参加を渡しました。Basisに戻ってください。");
    };
    addEventListener("message", onMessage);
    return () => removeEventListener("message", onMessage);
  }, []);

  const nativeBridgeUrl = () => {
    if (!details?.connection || !details.meeting?.id) return "";
    const { host, port, password, webSocketUri } = details.connection;
    if (!host || !port || !password) return "";
    const query = new URLSearchParams({
      password,
      meeting: details.meeting.id,
    });
    if (webSocketUri) query.set("websocketUri", webSocketUri);
    const deepLinkHost = host.includes(":") && !host.startsWith("[") ? `[${host}]` : host;
    const deepLink = `basisdemo://${deepLinkHost}:${port}?${query.toString()}`;
    const manifest = `${window.location.origin}/join/${encodeURIComponent(token)}/manifest`;
    return `http://127.0.0.1:56831/basis-join?config=${encodeURIComponent(manifest)}&link=${encodeURIComponent(deepLink)}`;
  };

  const openNative = () => {
    const bridge = nativeBridgeUrl();
    if (!bridge) {
      setError("この参加リンクには Basis アプリ用の接続情報がありません。");
      return;
    }
    setStatus("Basisに会議情報を渡しています…");
    const frame = document.getElementById("basis-join") as HTMLIFrameElement | null;
    if (frame) frame.src = bridge;
    window.setTimeout(() => setStatus("Basisが起動していることを確認してください。"), 2500);
  };

  const webJoinUrl = details?.connection?.webSocketUri && details.connection.serverInfoUri
    ? `${import.meta.env.VITE_WEB_CLIENT_ORIGIN || window.location.origin}/?basisMeeting=1&meetingUrl=${encodeURIComponent(`${window.location.origin}/join/${encodeURIComponent(token)}/manifest`)}`
    : "";

  return (
    <main className="join-shell">
      <section className="join-card">
        <Container>
          <SpaceBetween size="l">
            <Header variant="h1">{details?.meeting?.title ?? "会議に参加"}</Header>
            {error ? <Box color="text-status-error">{error}</Box> : <Box>{status}</Box>}
            {!error && details && (
              <SpaceBetween size="s">
                {webJoinUrl && <Button variant="primary" href={webJoinUrl} fullWidth>Webで参加</Button>}
                <Button onClick={openNative} fullWidth>
                  Basisアプリで参加
                </Button>
              </SpaceBetween>
            )}
            <Box color="text-body-secondary" fontSize="body-s">
              Web版はブラウザで開きます。Basisアプリを起動済みの場合は、アプリで参加できます。
            </Box>
          </SpaceBetween>
        </Container>
      </section>
      <iframe id="basis-join" title="Basis join bridge" hidden />
    </main>
  );
}

createRoot(document.getElementById("root")!).render(<JoinPage />);
