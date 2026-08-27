import { useEffect, useMemo, useState } from "react";
import { BrowserRouter, Route, Routes, useNavigate } from "react-router-dom";
import "@cloudscape-design/global-styles/index.css";
import Box from "@cloudscape-design/components/box";
import Button from "@cloudscape-design/components/button";
import ColumnLayout from "@cloudscape-design/components/column-layout";
import Container from "@cloudscape-design/components/container";
import ContentLayout from "@cloudscape-design/components/content-layout";
import CopyToClipboard from "@cloudscape-design/components/copy-to-clipboard";
import Form from "@cloudscape-design/components/form";
import FormField from "@cloudscape-design/components/form-field";
import Flashbar from "@cloudscape-design/components/flashbar";
import Header from "@cloudscape-design/components/header";
import Input from "@cloudscape-design/components/input";
import Link from "@cloudscape-design/components/link";
import SpaceBetween from "@cloudscape-design/components/space-between";
import StatusIndicator from "@cloudscape-design/components/status-indicator";
import Table from "@cloudscape-design/components/table";
import TopNavigation from "@cloudscape-design/components/top-navigation";
import Toggle from "@cloudscape-design/components/toggle";
import { createRoot } from "react-dom/client";
import { ControlPlaneApi, Health, Meeting, Organization, Provider, Server } from "./api";
import { validateBrowserEndpoints } from "./validation";
import "./styles.css";

type Notice = { type: "success" | "error"; text: string } | null;

type OrganizationForm = {
  displayName: string;
  googleEnabled: boolean;
  googleWebClientId: string;
  googleWebClientSecret: string;
  googleTokenEndpoint: string;
  googleNativeClientId: string;
  googleNativeClientSecret: string;
  googleDomains: string;
  oktaEnabled: boolean;
  oktaIssuer: string;
  oktaClientId: string;
  oktaClientSecret: string;
  oktaTokenEndpoint: string;
  oktaJwksUri: string;
  oktaGroups: string;
};

const csv = (value: string) =>
  value
    .split(",")
    .map((item) => item.trim())
    .filter(Boolean);
const find = (providers: Provider[], id: string) =>
  providers.find((provider) => provider.id === id);
const blankForm = (): OrganizationForm => ({
  displayName: "",
  googleEnabled: true,
  googleWebClientId: "",
  googleWebClientSecret: "",
  googleTokenEndpoint: "https://oauth2.googleapis.com/token",
  googleNativeClientId: "",
  googleNativeClientSecret: "",
  googleDomains: "",
  oktaEnabled: false,
  oktaIssuer: "",
  oktaClientId: "",
  oktaClientSecret: "",
  oktaTokenEndpoint: "",
  oktaJwksUri: "",
  oktaGroups: "",
});

const formFromOrganization = (organization: Organization): OrganizationForm => {
  const google = find(organization.providers, "google");
  const okta = find(organization.providers, "okta");
  return {
    displayName: organization.displayName ?? "",
    googleEnabled: Boolean(google?.webClientId),
    googleWebClientId: google?.webClientId ?? "",
    googleWebClientSecret: google?.webClientSecret ?? "",
    googleTokenEndpoint: google?.tokenEndpoint ?? "https://oauth2.googleapis.com/token",
    googleNativeClientId: google?.audience ?? "",
    googleNativeClientSecret: google?.clientSecret ?? "",
    googleDomains: (google?.allowedHostedDomains ?? []).join(", "),
    oktaEnabled: Boolean(okta?.issuer && okta?.audience && okta?.jwksUri),
    oktaIssuer: okta?.issuer ?? "",
    oktaClientId: okta?.audience ?? "",
    oktaClientSecret: okta?.clientSecret ?? "",
    oktaTokenEndpoint: okta?.tokenEndpoint ?? "",
    oktaJwksUri: okta?.jwksUri ?? "",
    oktaGroups: (okta?.allowedGroups ?? []).join(", "),
  };
};

const organizationFromForm = (form: OrganizationForm): Organization => {
  const providers: Provider[] = [];
  if (form.googleEnabled)
    providers.push({
      id: "google",
      label: "Google organization account",
      issuer: "https://accounts.google.com",
      audience: form.googleNativeClientId.trim() || undefined,
      clientSecret: form.googleNativeClientSecret || undefined,
      webClientId: form.googleWebClientId.trim(),
      webClientSecret: form.googleWebClientSecret || undefined,
      tokenEndpoint: form.googleTokenEndpoint.trim() || undefined,
      jwksUri: "https://www.googleapis.com/oauth2/v3/certs",
      allowedHostedDomains: csv(form.googleDomains),
      allowedGroups: [],
    });
  if (form.oktaEnabled)
    providers.push({
      id: "okta",
      label: "Okta",
      issuer: form.oktaIssuer.trim(),
      audience: form.oktaClientId.trim(),
      clientSecret: form.oktaClientSecret,
      webClientId: form.oktaClientId.trim(),
      webClientSecret: form.oktaClientSecret,
      tokenEndpoint: form.oktaTokenEndpoint.trim() || undefined,
      jwksUri: form.oktaJwksUri.trim(),
      allowedHostedDomains: [],
      allowedGroups: csv(form.oktaGroups),
    });
  return {
    displayName: form.displayName.trim(),
    defaultProviderId: providers[0]?.id ?? "",
    providers,
  };
};

const validateOrganizationForm = (form: OrganizationForm): string | null => {
  if (!form.googleEnabled && !form.oktaEnabled)
    return "Google organization account または Okta を少なくとも一つ有効にしてください。";
  if (form.googleEnabled && !form.googleWebClientId.trim())
    return "Google Web OAuth Client ID を入力してください。";
  if (form.googleEnabled && !form.googleTokenEndpoint.trim())
    return "Google OAuth token endpoint を入力してください。";
  if (
    form.oktaEnabled &&
    (!form.oktaIssuer.trim() ||
      !form.oktaClientId.trim() ||
      !form.oktaJwksUri.trim() ||
      !form.oktaTokenEndpoint.trim())
  )
    return "Okta の Issuer、OAuth Client ID、JWKS URL を入力してください。";
  return null;
};

function Page({
  children,
  notifications,
}: {
  children: React.ReactNode;
  notifications?: React.ReactNode;
}) {
  return (
    <div className="control-plane-shell">
      <div className="control-plane-top-navigation">
        <TopNavigation
          identity={{ href: "/admin", title: "SekaiMate Console" }}
        />
      </div>
      <main className="control-plane-main">
        <div className="control-plane-content">
          {notifications && <div className="control-plane-notifications">{notifications}</div>}
          <ContentLayout
            header={
              <Header
                variant="h1"
                description="組織の本人確認ルールと会議室への参加リンクを管理します。"
              >
                会議室の管理
              </Header>
            }
          >
            {children}
          </ContentLayout>
        </div>
      </main>
      <footer className="control-plane-footer">
        <span>SekaiMate Console</span>
        <a href="https://github.com/BasisVR/Basis" target="_blank" rel="noreferrer">
          SekaiMate · Basis-VR fork · upstream (BasisVR/Basis)
        </a>
      </footer>
    </div>
  );
}

function NoticeView({
  notice,
  onDismiss,
}: {
  notice: Notice;
  onDismiss(): void;
}) {
  const [items, setItems] = useState<Array<Notice & { id: string }>>([]);

  useEffect(() => {
    if (!notice) return;
    const id = `notice-${Date.now()}-${Math.random().toString(36).slice(2)}`;
    setItems((current) => [...current, { ...notice, id }]);
  }, [notice]);

  if (items.length === 0) return null;
  return (
    <Flashbar
      stackItems
      items={items.map((item) => ({
        id: item.id,
        type: item.type,
        content: item.text,
        dismissible: true,
        onDismiss: () => {
          setItems((current) =>
            current.filter((candidate) => candidate.id !== item.id),
          );
          onDismiss();
        },
      }))}
    />
  );
}

function SecretField({
  label,
  value,
  onChange,
  description,
}: {
  label: string;
  value: string;
  onChange(value: string): void;
  description?: string;
}) {
  const [visible, setVisible] = useState(false);
  return (
    <FormField label={label} description={description}>
      <SpaceBetween direction="horizontal" size="xs">
        <Input
          type={visible ? "text" : "password"}
          value={value}
          onChange={({ detail }) => onChange(detail.value)}
        />
        <Button onClick={() => setVisible(!visible)}>
          {visible ? "隠す" : "表示"}
        </Button>
      </SpaceBetween>
    </FormField>
  );
}

function IssuedLinkCard({
  title,
  description,
  url,
  statusText,
  validityText,
}: {
  title: string;
  description: string;
  url: string;
  statusText: string;
  validityText?: string;
}) {
  return (
    <Container header={<Header variant="h2" description={description}>{title}</Header>}>
      <SpaceBetween size="s">
        <StatusIndicator type="success">{statusText}</StatusIndicator>
        <Input value={url} readOnly />
        <SpaceBetween direction="horizontal" size="xs">
          <CopyToClipboard copyButtonText="URL をコピー" copyErrorText="コピーできませんでした" copySuccessText="コピーしました" textToCopy={url} />
          <Button iconName="external" href={url} target="_blank">URL を開く</Button>
        </SpaceBetween>
        {validityText && <Box variant="small" color="text-body-secondary">{validityText}</Box>}
      </SpaceBetween>
    </Container>
  );
}

// JoinLinkRow renders one shareable join URL with copy/open actions, or the
// reason it is unavailable, reusing IssuedLinkCard's layout inside a card
// that carries more than one URL.
function JoinLinkRow({
  label,
  description,
  url,
  emptyText,
}: {
  label: string;
  description: string;
  url: string;
  emptyText: string;
}) {
  return (
    <FormField label={label} description={description}>
      {url ? (
        <SpaceBetween size="xs">
          <Input value={url} readOnly />
          <SpaceBetween direction="horizontal" size="xs">
            <CopyToClipboard copyButtonText="URL をコピー" copyErrorText="コピーできませんでした" copySuccessText="コピーしました" textToCopy={url} />
            <Button iconName="external" href={url} target="_blank">URL を開く</Button>
          </SpaceBetween>
        </SpaceBetween>
      ) : (
        <Box color="text-status-warning">{emptyText}</Box>
      )}
    </FormField>
  );
}

// MeetingJoinLinks shows both join URLs for a just-created meeting. A managed
// meeting starts as "provisioning", so the card tracks the polled meeting
// record and swaps the waiting indicator for the URLs once it is joinable.
function MeetingJoinLinks({
  meeting,
  onDismiss,
}: {
  meeting: Meeting;
  onDismiss(): void;
}) {
  return (
    <Container
      header={
        <Header
          variant="h2"
          description="参加者へそのまま共有できる URL です。"
          actions={<Button onClick={onDismiss}>閉じる</Button>}
        >
          会議室「{meeting.title}」の参加 URL
        </Header>
      }
    >
      {meeting.invitationReady ? (
        <SpaceBetween size="l">
          <JoinLinkRow
            label="WebGL の参加 URL"
            description="ブラウザで Web 版を開いて参加します。"
            url={meeting.webJoinUrl ?? ""}
            emptyText="Web 版の配信元を appsettings.json の AllowedWebOrigins に設定すると表示されます。"
          />
          <JoinLinkRow
            label="Basis の参加 URL"
            description="参加ページを開き、Basis アプリで参加します。"
            url={meeting.joinUrl}
            emptyText="参加ページの URL を取得できませんでした。"
          />
        </SpaceBetween>
      ) : (
        <StatusIndicator type="in-progress">サーバーの起動を待っています。準備が完了すると参加 URL を表示します。</StatusIndicator>
      )}
    </Container>
  );
}

function OrganizationSettings({
  api,
  refresh,
}: {
  api: ControlPlaneApi;
  refresh(): Promise<void>;
}) {
  const navigate = useNavigate();
  const [form, setForm] = useState<OrganizationForm>(blankForm);
  const [notice, setNotice] = useState<Notice>(null);
  const [busy, setBusy] = useState(false);
  const update = <K extends keyof OrganizationForm>(
    key: K,
    value: OrganizationForm[K],
  ) => setForm((current) => ({ ...current, [key]: value }));

  useEffect(() => {
    void api
      .organization()
      .then((organization) => setForm(formFromOrganization(organization)))
      .catch((error) => setNotice({ type: "error", text: error.message }));
  }, [api]);

  const save = async () => {
    const validationError = validateOrganizationForm(form);
    if (validationError) {
      setNotice({ type: "error", text: validationError });
      return;
    }
    setBusy(true);
    try {
      await api.saveOrganization(organizationFromForm(form));
      await refresh();
      setNotice({
        type: "success",
        text: "組織のログイン設定を保存しました。新しく発行する参加リンクに反映されます。",
      });
    } catch (error) {
      setNotice({
        type: "error",
        text: error instanceof Error ? error.message : "保存できませんでした。",
      });
    } finally {
      setBusy(false);
    }
  };

  return (
    <Page
      notifications={
        <NoticeView notice={notice} onDismiss={() => setNotice(null)} />
      }
    >
      <SpaceBetween size="l">
        <Button iconName="arrow-left" onClick={() => navigate("/")}>
          会議一覧へ戻る
        </Button>
        <Form
          header={
            <Header
              variant="h2"
              description="この設定は組織共通です。会議ごとに再設定する必要はありません。"
            >
              組織のログイン設定
            </Header>
          }
          actions={
            <SpaceBetween direction="horizontal" size="xs">
              <Button
                variant="primary"
                onClick={() => void save()}
                loading={busy}
              >
                保存する
              </Button>
            </SpaceBetween>
          }
        >
          <SpaceBetween size="l">
            <Container header={<Header variant="h2">組織</Header>}>
              <FormField label="表示名">
                <Input
                  value={form.displayName}
                  onChange={({ detail }) => update("displayName", detail.value)}
                  placeholder="Mimifuwa"
                />
              </FormField>
            </Container>
            <Container
              header={
                <Header
                  variant="h2"
                  description="Google組織アカウントのhosted domainで参加者を限定できます。"
                >
                  Google organization account
                </Header>
              }
            >
              <SpaceBetween size="m">
                <Toggle
                  checked={form.googleEnabled}
                  onChange={({ detail }) =>
                    update("googleEnabled", detail.checked)
                  }
                >
                  Google organization accountを有効にする
                </Toggle>
                <ColumnLayout columns={2}>
                  <FormField
                    label="Web OAuth Client ID"
                    description="Google Cloud Consoleで種類をウェブアプリケーションとして作成したClient IDです。"
                  >
                    <Input
                      value={form.googleWebClientId}
                      onChange={({ detail }) =>
                        update("googleWebClientId", detail.value)
                      }
                      placeholder="…apps.googleusercontent.com"
                    />
                  </FormField>
                  <FormField label="Token endpoint" description="Authorization Code / refresh token の交換先です。">
                    <Input
                      value={form.googleTokenEndpoint}
                      onChange={({ detail }) => update("googleTokenEndpoint", detail.value)}
                      placeholder="https://oauth2.googleapis.com/token"
                    />
                  </FormField>
                  <FormField
                    label="許可ドメイン"
                    description="カンマ区切り。*はGoogle組織アカウントを必須にします。空欄なら全Googleアカウントを許可します。"
                  >
                    <Input
                      value={form.googleDomains}
                      onChange={({ detail }) =>
                        update("googleDomains", detail.value)
                      }
                      placeholder="mimifuwa.cc"
                    />
                  </FormField>
                  <FormField
                    label="Web OAuth Client secret"
                    description="Brokerのサーバー側設定に保存します。WebGLや参加者向け設定には返しません。空欄にすると既存の環境変数を使います。"
                  >
                    <Input
                      type="password"
                      value={form.googleWebClientSecret}
                      onChange={({ detail }) =>
                        update("googleWebClientSecret", detail.value)
                      }
                      placeholder="Google OAuth Client secret"
                    />
                  </FormField>
                </ColumnLayout>
              </SpaceBetween>
            </Container>
            <Container
              header={
                <Header
                  variant="h2"
                  description="Okta を使わない場合は無効のままにしてください。"
                >
                  Okta
                </Header>
              }
            >
              <SpaceBetween size="m">
                <Toggle
                  checked={form.oktaEnabled}
                  onChange={({ detail }) =>
                    update("oktaEnabled", detail.checked)
                  }
                >
                  Okta を有効にする
                </Toggle>
                <ColumnLayout columns={2}>
                  <FormField label="Issuer">
                    <Input
                      value={form.oktaIssuer}
                      onChange={({ detail }) =>
                        update("oktaIssuer", detail.value)
                      }
                      placeholder="https://YOUR_OKTA_DOMAIN/oauth2/default"
                    />
                  </FormField>
                  <FormField label="OAuth Client ID">
                    <Input
                      value={form.oktaClientId}
                      onChange={({ detail }) =>
                        update("oktaClientId", detail.value)
                      }
                    />
                  </FormField>
                  <SecretField
                    label="OAuth Client secret"
                    value={form.oktaClientSecret}
                    onChange={(value) => update("oktaClientSecret", value)}
                  />
                  <FormField label="JWKS URL">
                    <Input
                      value={form.oktaJwksUri}
                      onChange={({ detail }) =>
                        update("oktaJwksUri", detail.value)
                      }
                      placeholder="https://YOUR_OKTA_DOMAIN/oauth2/default/v1/keys"
                    />
                  </FormField>
                  <FormField label="Token endpoint" description="Authorization Code / refresh token の交換先です。">
                    <Input
                      value={form.oktaTokenEndpoint}
                      onChange={({ detail }) => update("oktaTokenEndpoint", detail.value)}
                      placeholder="https://YOUR_OKTA_DOMAIN/oauth2/default/v1/token"
                    />
                  </FormField>
                  <FormField label="許可グループ" description="カンマ区切り。">
                    <Input
                      value={form.oktaGroups}
                      onChange={({ detail }) =>
                        update("oktaGroups", detail.value)
                      }
                      placeholder="basis-users, admins"
                    />
                  </FormField>
                </ColumnLayout>
              </SpaceBetween>
            </Container>
          </SpaceBetween>
        </Form>
      </SpaceBetween>
    </Page>
  );
}

function Meetings({
  api,
  meetings,
  refresh,
}: {
  api: ControlPlaneApi;
  meetings: Meeting[];
  refresh(): Promise<void>;
}) {
  const navigate = useNavigate();
  const [notice, setNotice] = useState<Notice>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [health, setHealth] = useState<Health | null>(null);
  const [createTitle, setCreateTitle] = useState("");
  const [createHost, setCreateHost] = useState("");
  const [createPort, setCreatePort] = useState("4296");
  const [createWebSocketUri, setCreateWebSocketUri] = useState("");
  const [createServerInfoUri, setCreateServerInfoUri] = useState("");
  const [creating, setCreating] = useState(false);
  // Holds the meeting returned by create; the polled list entry takes over as
  // soon as it appears so the card follows the meeting to "参加可能".
  const [createdMeeting, setCreatedMeeting] = useState<Meeting | null>(null);
  const createdJoinLinks = createdMeeting
    ? meetings.find((meeting) => meeting.id === createdMeeting.id) ?? createdMeeting
    : null;
  const load = async () => {
    try {
      await refresh();
    } catch (error) {
      setNotice({ type: "error", text: error instanceof Error ? error.message : "会議室を読み込めませんでした。" });
    }
    try {
      setHealth(await api.health());
    } catch (error) {
      setNotice({ type: "error", text: error instanceof Error ? error.message : "ヘルス状態を取得できませんでした。" });
    }
  };
  useEffect(() => {
    void load()
      .catch((error) =>
        setNotice({
          type: "error",
          text:
            error instanceof Error
              ? error.message
              : "会議室を読み込めませんでした。",
        }),
      )
      .finally(() => setLoading(false));
    const timer = window.setInterval(() => { void load(); }, 5000);
    return () => window.clearInterval(timer);
  }, [api]);
  const invite = async (meeting: Meeting) => {
    setBusy(meeting.id);
    try {
      const result = await api.issueInvitation(meeting.id);
      await navigator.clipboard?.writeText(result.url);
      setNotice({
        type: "success",
        text: `「${meeting.title}」の参加リンクをコピーしました。`,
      });
    } catch (error) {
      setNotice({
        type: "error",
        text:
          error instanceof Error
            ? error.message
            : "参加リンクを発行できませんでした。",
      });
    } finally {
      setBusy(null);
    }
  };
  const create = async () => {
    const title = createTitle.trim();
    if (!title) {
      setNotice({ type: "error", text: "会議室名を入力してください。" });
      return;
    }
    const port = Number(createPort);
    if (!Number.isInteger(port) || port < 1 || port > 65535) {
      setNotice({ type: "error", text: "UDP ポートは 1〜65535 の整数で入力してください。" });
      return;
    }
    const endpointError = validateBrowserEndpoints(createWebSocketUri, createServerInfoUri);
    if (endpointError) {
      setNotice({ type: "error", text: endpointError });
      return;
    }
    setCreating(true);
    try {
      const created = await api.createMeeting({
        title,
        ...(createHost.trim() ? { host: createHost.trim() } : {}),
        port,
        ...(createWebSocketUri.trim() ? { webSocketUri: createWebSocketUri.trim(), serverInfoUri: createServerInfoUri.trim() } : {}),
      });
      setCreateTitle("");
      setCreateHost("");
      setCreatedMeeting(created);
      setNotice({ type: "success", text: `会議室「${title}」を作成しました。起動中は自動更新します。` });
      await load();
    } catch (error) {
      setNotice({ type: "error", text: error instanceof Error ? error.message : "会議室を作成できませんでした。" });
    } finally {
      setCreating(false);
    }
  };
  const remove = async (meeting: Meeting) => {
    if (!window.confirm(`会議室「${meeting.title}」を削除しますか？`)) return;
    setBusy(meeting.id);
    try {
      await api.deleteMeeting(meeting.id);
      setCreatedMeeting((current) => (current?.id === meeting.id ? null : current));
      setNotice({ type: "success", text: `会議室「${meeting.title}」を削除しました。` });
      await load();
    } catch (error) {
      setNotice({ type: "error", text: error instanceof Error ? error.message : "会議室を削除できませんでした。" });
    } finally {
      setBusy(null);
    }
  };
  return (
    <Page
      notifications={
        <NoticeView notice={notice} onDismiss={() => setNotice(null)} />
      }
    >
      <SpaceBetween size="l">
        {createdJoinLinks && <MeetingJoinLinks meeting={createdJoinLinks} onDismiss={() => setCreatedMeeting(null)} />}
        <Table
          columnDefinitions={[
            {
              id: "title",
              header: "会議室",
              cell: (meeting) => (
                <SpaceBetween size="xxs">
                  <Box fontWeight="bold">{meeting.title}</Box>
                  <Box variant="small" color="text-body-secondary">
                    {meeting.id}
                  </Box>
                </SpaceBetween>
              ),
            },
            {
              id: "status",
              header: "状態",
              cell: (meeting) => <SpaceBetween size="xxs"><StatusIndicator type={meeting.invitationReady ? "success" : meeting.status === "error" ? "error" : "warning"}>{meeting.invitationReady ? "参加可能" : meeting.status || "起動待ち"}</StatusIndicator><Box variant="small">{meeting.statusDetail}</Box></SpaceBetween>,
            },
            {
              id: "endpoint",
              header: "接続先",
              cell: (meeting) => `${meeting.host}:${meeting.port}`,
            },
            {
              id: "browser-endpoints",
              header: "WebGL 接続先",
              cell: (meeting) => (
                <SpaceBetween size="xxs">
                  <Box variant="small">WebSocket: {meeting.webSocketUri ?? "未設定"}</Box>
                  <Box variant="small">Server Info: {meeting.serverInfoUri ?? "未設定"}</Box>
                </SpaceBetween>
              ),
            },
            {
              id: "join-urls",
              header: "参加 URL",
              cell: (meeting) => !meeting.invitationReady ? (
                <Box variant="small" color="text-body-secondary">起動待ち</Box>
              ) : (
                <SpaceBetween size="xxs">
                  {meeting.webJoinUrl
                    ? <Link external externalIconAriaLabel="新しいタブで開きます" href={meeting.webJoinUrl}>WebGL で参加</Link>
                    : <Box variant="small" color="text-body-secondary">WebGL: 未設定</Box>}
                  <Link external externalIconAriaLabel="新しいタブで開きます" href={meeting.joinUrl}>Basis で参加</Link>
                </SpaceBetween>
              ),
            },
            {
              id: "actions",
              header: "",
              cell: (meeting) => (
                <SpaceBetween size="xs"><Button variant="primary" disabled={!meeting.invitationReady} loading={busy === meeting.id} onClick={() => void invite(meeting)}>参加リンクをコピー</Button><Button variant="link" loading={busy === meeting.id} onClick={() => void remove(meeting)}>削除</Button></SpaceBetween>
              ),
            },
          ]}
          items={meetings}
          loading={loading}
          loadingText="会議室を読み込んでいます"
          empty={
            <Box textAlign="center" color="inherit">
              <b>会議室がありません</b>
              <Box padding={{ bottom: "s" }} variant="p" color="inherit">
                Docker Compose を起動するとローカル会議室が登録されます。
              </Box>
            </Box>
          }
          header={
            <Header
              variant="h2"
              description="参加リンクは会議ごとに発行します。"
              actions={
                <SpaceBetween direction="horizontal" size="xs">
                  <Button
                    onClick={() => {
                      setLoading(true);
                      void refresh()
                        .catch((error) =>
                          setNotice({
                            type: "error",
                            text:
                              error instanceof Error
                                ? error.message
                                : "会議室を読み込めませんでした。",
                          }),
                        )
                        .finally(() => setLoading(false));
                    }}
                  >
                    更新
                  </Button>
                  <Button onClick={() => navigate("/organization")}>
                    組織のログイン設定
                  </Button>
                  <Button onClick={() => navigate("/servers")}>
                    サーバー設定
                  </Button>
                </SpaceBetween>
              }
            >
              会議室
            </Header>
          }
        />
        {health && <Container header={<Header variant="h2">Concierge の状態</Header>}>
          <SpaceBetween size="s"><StatusIndicator type={health.status === "ready" ? "success" : "warning"}>{health.status === "ready" ? "正常" : "準備中"}</StatusIndicator>{health.error && <Box color="text-status-warning">{health.error}</Box>}<Box variant="small">対象サーバー: {health.servers.length}（準備完了 {health.servers.filter((server) => server.ready).length}）</Box></SpaceBetween>
        </Container>}
        <Form header={<Header variant="h2" description="BASIS_CONTROL_PLANE_ALLOW_MANUAL_MEETINGS=true の環境で利用できます。">会議室を作成</Header>} actions={<Button variant="primary" loading={creating} onClick={() => void create()}>作成する</Button>}>
          <ColumnLayout columns={2}><FormField label="会議室名"><Input value={createTitle} onChange={({ detail }) => setCreateTitle(detail.value)} placeholder="Team room" /></FormField><FormField label="接続先ホスト（任意）" description="空欄なら Kubernetes でプロビジョニングします。"><Input value={createHost} onChange={({ detail }) => setCreateHost(detail.value)} placeholder="room.example.com" /></FormField><FormField label="UDP ポート"><Input value={createPort} onChange={({ detail }) => setCreatePort(detail.value)} inputMode="numeric" /></FormField><FormField label="WebSocket URI（任意）"><Input value={createWebSocketUri} onChange={({ detail }) => setCreateWebSocketUri(detail.value)} placeholder="wss://room.example/basis" /></FormField><FormField label="Server Info URI（任意）"><Input value={createServerInfoUri} onChange={({ detail }) => setCreateServerInfoUri(detail.value)} placeholder="https://room.example/server-info" /></FormField></ColumnLayout>
        </Form>
      </SpaceBetween>
    </Page>
  );
}

function ServerSettings({ api }: { api: ControlPlaneApi }) {
  const navigate = useNavigate();
  const [servers, setServers] = useState<Server[]>([]);
  const [selected, setSelected] = useState<Server | null>(null);
  const [webSocketUri, setWebSocketUri] = useState("");
  const [serverInfoUri, setServerInfoUri] = useState("");
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<Notice>(null);
  const [newServerId, setNewServerId] = useState("");
  const [newTicketKeyEnv, setNewTicketKeyEnv] = useState("");
  const [newTransportKeyEnv, setNewTransportKeyEnv] = useState("");
  const [creating, setCreating] = useState(false);
  const [enrollment, setEnrollment] = useState<{ serverId: string; url: string; expiresInSeconds: number } | null>(null);

  const load = async () => {
    setLoading(true);
    try {
      const next = await api.listServers();
      setServers(next);
      if (selected) {
        const refreshed = next.find((server) => server.id === selected.id);
        if (refreshed) setSelected(refreshed);
      }
    } catch (error) {
      setNotice({ type: "error", text: error instanceof Error ? error.message : "サーバーを読み込めませんでした。" });
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { void load(); }, []);

  const edit = (server: Server) => {
    setSelected(server);
    setWebSocketUri(server.webSocketUri ?? "");
    setServerInfoUri(server.serverInfoUri ?? "");
    setNotice(null);
  };

  const save = async () => {
    if (!selected) return;
    const error = validateBrowserEndpoints(webSocketUri, serverInfoUri);
    if (error) {
      setNotice({ type: "error", text: error });
      return;
    }
    setBusy(true);
    try {
      const updated = {
        ...selected,
        webSocketUri: webSocketUri.trim() || undefined,
        serverInfoUri: serverInfoUri.trim() || undefined,
      };
      await api.saveServer(updated);
      setServers((current) => current.map((server) => server.id === updated.id ? updated : server));
      setSelected(updated);
      setNotice({ type: "success", text: `サーバー「${updated.id}」の WebGL 接続先を保存しました。` });
    } catch (error) {
      setNotice({ type: "error", text: error instanceof Error ? error.message : "サーバー設定を保存できませんでした。" });
    } finally {
      setBusy(false);
    }
  };

  const create = async () => {
    const id = newServerId.trim();
    if (!id || !newTicketKeyEnv.trim() || !newTransportKeyEnv.trim()) {
      setNotice({ type: "error", text: "サーバー ID と二つのキー環境変数名を入力してください。" });
      return;
    }
    setCreating(true);
    try {
      const organization = await api.organization();
      const server: Server = {
        id,
        ticketSigningKeyEnvironmentVariable: newTicketKeyEnv.trim(),
        transportPublicKeyEnvironmentVariable: newTransportKeyEnv.trim(),
        providers: organization.providers,
        ready: false,
        hasTicketSigningKey: false,
        hasTransportPublicKey: false,
      };
      await api.saveServer(server);
      setNewServerId("");
      setNewTicketKeyEnv("");
      setNewTransportKeyEnv("");
      setNotice({ type: "success", text: `サーバー「${id}」を作成しました。` });
      await load();
    } catch (error) {
      setNotice({ type: "error", text: error instanceof Error ? error.message : "サーバーを作成できませんでした。" });
    } finally {
      setCreating(false);
    }
  };

  const remove = async (server: Server) => {
    if (!window.confirm(`静的サーバー「${server.id}」を削除しますか？`)) return;
    setBusy(true);
    try {
      await api.deleteServer(server.id);
      setNotice({ type: "success", text: `サーバー「${server.id}」を削除しました。` });
      if (selected?.id === server.id) setSelected(null);
      await load();
    } catch (error) {
      setNotice({ type: "error", text: error instanceof Error ? error.message : "サーバーを削除できませんでした。" });
    } finally {
      setBusy(false);
    }
  };

  const issueEnrollment = async (server: Server) => {
    setBusy(true);
    try {
      const result = await api.issueEnrollment(server.id);
      setEnrollment({ serverId: server.id, ...result });
      await navigator.clipboard?.writeText(result.url);
      setNotice({ type: "success", text: `「${server.id}」の登録リンクを発行してコピーしました。` });
    } catch (error) {
      setNotice({ type: "error", text: error instanceof Error ? error.message : "登録リンクを発行できませんでした。" });
    } finally {
      setBusy(false);
    }
  };

  return (
    <Page notifications={<NoticeView notice={notice} onDismiss={() => setNotice(null)} />}>
      <SpaceBetween size="l">
        <Button iconName="arrow-left" onClick={() => navigate("/")}>会議一覧へ戻る</Button>
        <Table
          loading={loading}
          loadingText="サーバーを読み込んでいます"
          items={servers}
          columnDefinitions={[
            { id: "id", header: "サーバー", cell: (server) => server.id },
            { id: "ready", header: "状態", cell: (server) => <StatusIndicator type={server.ready ? "success" : "warning"}>{server.ready ? "利用可能" : "未準備"}</StatusIndicator> },
            { id: "websocket", header: "WebSocket URI", cell: (server) => server.webSocketUri ?? "未設定" },
            { id: "server-info", header: "Server Info URI", cell: (server) => server.serverInfoUri ?? "未設定" },
            { id: "actions", header: "操作", cell: (server) => <SpaceBetween size="xs"><Button onClick={() => edit(server)}>編集</Button><Button onClick={() => void issueEnrollment(server)} loading={busy}>登録リンク</Button><Button variant="link" onClick={() => void remove(server)} loading={busy}>削除</Button></SpaceBetween> },
          ]}
          header={<Header variant="h2" actions={<Button onClick={() => void load()}>更新</Button>}>静的サーバー</Header>}
          empty={<Box textAlign="center">サーバーがありません。</Box>}
        />
        {enrollment && <IssuedLinkCard title="サーバー登録リンク" description={`「${enrollment.serverId}」を Basis Server に登録するための一回限りのリンクです。`} url={enrollment.url} statusText="発行済み" validityText={`${Math.floor(enrollment.expiresInSeconds / 60)} 分で期限切れになります。`} />}
        <Form header={<Header variant="h2" description="プロバイダーは組織設定から引き継ぎます。キー環境変数は Concierge の実行環境に設定してください。">静的サーバーを追加</Header>} actions={<Button variant="primary" loading={creating} onClick={() => void create()}>追加する</Button>}>
          <ColumnLayout columns={2}><FormField label="サーバー ID"><Input value={newServerId} onChange={({ detail }) => setNewServerId(detail.value)} placeholder="production-1" /></FormField><FormField label="チケット署名キー環境変数"><Input value={newTicketKeyEnv} onChange={({ detail }) => setNewTicketKeyEnv(detail.value)} placeholder="BASIS_TICKET_SIGNING_KEY" /></FormField><FormField label="Transport 公開キー環境変数"><Input value={newTransportKeyEnv} onChange={({ detail }) => setNewTransportKeyEnv(detail.value)} placeholder="BASIS_TRANSPORT_PUBLIC_KEY" /></FormField></ColumnLayout>
        </Form>
        {selected && (
          <Form
            header={<Header variant="h2" description="WebGL ブラウザ接続では両方の URI を設定してください。">「{selected.id}」の WebGL 接続先</Header>}
            actions={<SpaceBetween direction="horizontal" size="xs"><Button onClick={() => setSelected(null)}>キャンセル</Button><Button variant="primary" loading={busy} onClick={() => void save()}>保存する</Button></SpaceBetween>}
          >
            <SpaceBetween size="m">
              <FormField label="WebSocket URI" description="例: wss://room.example/basis。ローカル開発時のみ ws://localhost/... を利用できます。">
                <Input value={webSocketUri} onChange={({ detail }) => setWebSocketUri(detail.value)} placeholder="wss://room.example/basis" />
              </FormField>
              <FormField label="Server Info URI" description="例: https://room.example/server-info。WebSocket URI と同時に設定します。">
                <Input value={serverInfoUri} onChange={({ detail }) => setServerInfoUri(detail.value)} placeholder="https://room.example/server-info" />
              </FormField>
            </SpaceBetween>
          </Form>
        )}
      </SpaceBetween>
    </Page>
  );
}

function AdminLogin({ onLogin }: { onLogin(token: string): void }) {
  const [token, setToken] = useState("");
  return (
    <Page>
      <Container header={<Header variant="h2">管理者認証</Header>}>
        <SpaceBetween size="m">
          <SecretField
            label="管理トークン"
            value={token}
            onChange={setToken}
            description="サーバーのBASIS_SSO_ADMIN_TOKENを入力してください。ブラウザを閉じると消去されます。"
          />
          <Button variant="primary" disabled={token.length < 32} onClick={() => onLogin(token)}>
            ログイン
          </Button>
        </SpaceBetween>
      </Container>
    </Page>
  );
}

function AdminApp() {
  const [adminToken, setAdminToken] = useState(() => sessionStorage.getItem("basis.sso.adminToken") ?? "");
  const [meetings, setMeetings] = useState<Meeting[]>([]);
  const api = useMemo(
    () => new ControlPlaneApi(adminToken, () => {
      sessionStorage.removeItem("basis.sso.adminToken");
      setAdminToken("");
    }),
    [adminToken],
  );
  if (!adminToken)
    return <AdminLogin onLogin={(token) => {
      sessionStorage.setItem("basis.sso.adminToken", token);
      setAdminToken(token);
    }} />;
  const refresh = async () => {
    const next = await api.listMeetings();
    setMeetings(next);
  };
  return (
    <Routes>
      <Route
        path="/"
        element={<Meetings api={api} meetings={meetings} refresh={refresh} />}
      />
      <Route
        path="/organization"
        element={<OrganizationSettings api={api} refresh={refresh} />}
      />
      <Route path="/servers" element={<ServerSettings api={api} />} />
    </Routes>
  );
}

const routerBase =
  import.meta.env.BASE_URL === "/"
    ? "/"
    : import.meta.env.BASE_URL.replace(/\/$/, "");

createRoot(document.querySelector("#root")!).render(
  <BrowserRouter basename={routerBase}>
    <AdminApp />
  </BrowserRouter>,
);
