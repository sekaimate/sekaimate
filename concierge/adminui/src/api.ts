export type Provider = {
  id: string;
  label?: string;
  issuer: string;
  audience?: string;
  clientSecret?: string;
  webClientId?: string;
  webClientSecret?: string;
  tokenEndpoint?: string;
  jwksUri: string;
  allowedHostedDomains?: string[];
  allowedGroups?: string[];
};

export type Organization = {
  displayName?: string;
  defaultProviderId?: string;
  providers: Provider[];
};

export type Meeting = {
  id: string;
  title: string;
  status: string;
  statusDetail: string;
  host: string;
  port: number;
  createdAt: string;
  updatedAt: string;
  joinUrl: string;
  invitationReady: boolean;
  webSocketUri?: string;
  serverInfoUri?: string;
};

export type HealthServer = {
  id: string;
  ready: boolean;
  providers: string[];
};

export type Health = {
  status: string;
  error?: string;
  servers: HealthServer[];
};

export type Server = {
  id: string;
  ticketSigningKeyEnvironmentVariable?: string;
  transportPublicKeyEnvironmentVariable?: string;
  providers: Provider[];
  ready: boolean;
  hasTicketSigningKey: boolean;
  hasTransportPublicKey: boolean;
  webSocketUri?: string;
  serverInfoUri?: string;
};

export class ControlPlaneApi {
  constructor(private readonly adminToken: string) {}

  private async request<T>(path: string, init: RequestInit = {}): Promise<T> {
    const headers = new Headers(init.headers);
    headers.set("Authorization", `Bearer ${this.adminToken}`);
    const response = await fetch(`/api${path}`, {
      ...init,
      headers,
      cache: "no-store",
    });
    if (!response.ok) {
      const raw = await response.text();
      let detail = raw;
      try {
        const body: unknown = JSON.parse(raw);
        detail = typeof body === "object" && body !== null && "error" in body ? String(body.error) : raw;
      } catch { }
      throw new Error(detail || `Request failed (${response.status})`);
    }
    if (response.status === 204) return undefined as T;
    return response.json() as Promise<T>;
  }

  listMeetings() { return this.request<Meeting[]>("/admin/meetings"); }
  health() {
    return fetch("/health", { cache: "no-store" }).then(async (response) => {
      const body = await response.json() as Health;
      if (!body || !Array.isArray(body.servers)) throw new Error("Health response was invalid");
      return body;
    });
  }
  createMeeting(input: { title: string; host?: string; port?: number; webSocketUri?: string; serverInfoUri?: string }) {
    return this.request<Meeting>("/admin/meetings", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(input),
    });
  }
  deleteMeeting(id: string) { return this.request<void>(`/admin/meetings/${encodeURIComponent(id)}`, { method: "DELETE" }); }
  listServers() { return this.request<Server[]>("/admin/servers"); }
  saveServer(server: Server) {
    return this.request<void>(`/admin/servers/${encodeURIComponent(server.id)}`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        id: server.id,
        ticketSigningKeyEnvironmentVariable: server.ticketSigningKeyEnvironmentVariable,
        transportPublicKeyEnvironmentVariable: server.transportPublicKeyEnvironmentVariable,
        providers: server.providers,
        webSocketUri: server.webSocketUri ?? "",
        serverInfoUri: server.serverInfoUri ?? "",
      }),
    });
  }
  deleteServer(id: string) { return this.request<void>(`/admin/servers/${encodeURIComponent(id)}`, { method: "DELETE" }); }
  organization() { return this.request<Organization>("/admin/organization"); }
  saveOrganization(organization: Organization) {
    return this.request<void>("/admin/organization", { method: "PUT", headers: { "Content-Type": "application/json" }, body: JSON.stringify(organization) });
  }
  issueInvitation(meetingId: string) { return this.request<{ url: string; meetingId: string }>(`/admin/meetings/${encodeURIComponent(meetingId)}/invitations`, { method: "POST" }); }
  issueEnrollment(serverId: string) {
    return this.request<{ url: string; expiresInSeconds: number }>(`/admin/enrollment/${encodeURIComponent(serverId)}`,
      { method: "POST", body: "" });
  }
}
