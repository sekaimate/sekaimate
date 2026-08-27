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
  title: string;
  webJoinUrl: string;
  nativeBridgeUrl: string;
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
    fetch(`/join/${encodeURIComponent(token)}/details`, { cache: "no-store" })
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

  const openNative = () => {
    if (!details) return;
    setStatus("Basisに会議情報を渡しています…");
    const frame = document.getElementById("basis-join") as HTMLIFrameElement | null;
    if (frame) frame.src = details.nativeBridgeUrl;
    window.setTimeout(() => setStatus("Basisが起動していることを確認してください。"), 2500);
  };

  return (
    <main className="join-shell">
      <section className="join-card">
        <Container>
          <SpaceBetween size="l">
            <Header variant="h1">{details?.title ?? "会議に参加"}</Header>
            {error ? <Box color="text-status-error">{error}</Box> : <Box>{status}</Box>}
            {!error && details && (
              <SpaceBetween size="s">
                <Button variant="primary" href={details.webJoinUrl || undefined} fullWidth>
                  Webで参加
                </Button>
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
