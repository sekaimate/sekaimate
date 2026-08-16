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
import SpaceBetween from "@cloudscape-design/components/space-between";
import StatusIndicator from "@cloudscape-design/components/status-indicator";
import Table from "@cloudscape-design/components/table";
import TopNavigation from "@cloudscape-design/components/top-navigation";
import Toggle from "@cloudscape-design/components/toggle";
import { createRoot } from "react-dom/client";
import { ControlPlaneApi, Meeting, Organization, Provider } from "./api";
import "./styles.css";

type Notice = { type: "success" | "error"; text: string } | null;

type OrganizationForm = {
  displayName: string;
  googleEnabled: boolean;
  googleWebClientId: string;
  googleWebClientSecret: string;
  googleNativeClientId: string;
  googleNativeClientSecret: string;
  googleDomains: string;
  oktaEnabled: boolean;
  oktaIssuer: string;
  oktaClientId: string;
  oktaClientSecret: string;
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
  googleNativeClientId: "",
  googleNativeClientSecret: "",
  googleDomains: "",
  oktaEnabled: false,
  oktaIssuer: "",
  oktaClientId: "",
  oktaClientSecret: "",
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
    googleNativeClientId: google?.audience ?? "",
    googleNativeClientSecret: google?.clientSecret ?? "",
    googleDomains: (google?.allowedHostedDomains ?? []).join(", "),
    oktaEnabled: Boolean(okta?.issuer && okta?.audience && okta?.jwksUri),
    oktaIssuer: okta?.issuer ?? "",
    oktaClientId: okta?.audience ?? "",
    oktaClientSecret: okta?.clientSecret ?? "",
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
      tokenEndpoint: "https://oauth2.googleapis.com/token",
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
  if (
    form.oktaEnabled &&
    (!form.oktaIssuer.trim() ||
      !form.oktaClientId.trim() ||
      !form.oktaJwksUri.trim())
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
          identity={{ href: "/", title: "SekaiMate Console" }}
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
  const [enrollmentUrl, setEnrollmentUrl] = useState<string | null>(null);
  const [enrollmentIssued, setEnrollmentIssued] = useState(false);
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

  const issueEnrollment = async () => {
    setBusy(true);
    try {
      const result = await api.issueEnrollment("local");
      setEnrollmentUrl(result.url);
      setEnrollmentIssued(true);
    } catch (error) {
      setNotice({
        type: "error",
        text:
          error instanceof Error
            ? error.message
            : "組織設定 URL を発行できませんでした。",
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
                formAction="none"
                onClick={() => void issueEnrollment()}
                disabled={busy}
              >
                組織設定 URL を発行
              </Button>
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
        {enrollmentUrl && <IssuedLinkCard title="組織設定 URL" description="会議に入らず、この URL を開いた端末の Basis に組織設定だけを適用します。" url={enrollmentUrl} statusText="組織設定 URL を発行しました。必要な端末で URL を開いてください。" validityText="この URL は 10 分間・一回限りです。" />}
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
  useEffect(() => {
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
  }, []);
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
  return (
    <Page
      notifications={
        <NoticeView notice={notice} onDismiss={() => setNotice(null)} />
      }
    >
      <SpaceBetween size="l">
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
              cell: (meeting) => (
                <StatusIndicator
                  type={meeting.invitationReady ? "success" : "warning"}
                >
                  {meeting.invitationReady ? "参加可能" : "起動待ち"}
                </StatusIndicator>
              ),
            },
            {
              id: "endpoint",
              header: "接続先",
              cell: (meeting) => `${meeting.host}:${meeting.port}`,
            },
            {
              id: "actions",
              header: "",
              cell: (meeting) => (
                <Button
                  variant="primary"
                  disabled={!meeting.invitationReady}
                  loading={busy === meeting.id}
                  onClick={() => void invite(meeting)}
                >
                  参加リンクをコピー
                </Button>
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
                </SpaceBetween>
              }
            >
              会議室
            </Header>
          }
        />
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
  const api = useMemo(() => new ControlPlaneApi(adminToken), [adminToken]);
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
