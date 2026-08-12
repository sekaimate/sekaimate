export type Provider = {
  id: string;
  label?: string;
  issuer: string;
  audience: string;
  clientSecret?: string;
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
};

export class ControlPlaneApi {
  private async request<T>(path: string, init: RequestInit = {}): Promise<T> {
    const response = await fetch(`/api${path}`, {
      ...init,
      headers: init.headers,
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
