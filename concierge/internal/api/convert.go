package api

import (
	"net"
	"net/url"
	"strings"

	"github.com/sekaimate/sekaimate/concierge/internal/config"
	"github.com/sekaimate/sekaimate/concierge/internal/controlplane"
)

func providerConfigToAPI(p config.ProviderConfig) ProviderOptions {
	out := ProviderOptions{
		Id:       strPtr(p.Id),
		Issuer:   strPtr(p.Issuer),
		Audience: strPtr(p.Audience),
		JwksUri:  strPtr(p.JwksUri),
	}
	if p.Label != "" {
		out.Label = strPtr(p.Label)
	}
	if p.ClientSecret != "" {
		out.ClientSecret = strPtr(p.ClientSecret)
	}
	out.AllowedHostedDomains = strSlicePtr(p.AllowedHostedDomains)
	out.AllowedGroups = strSlicePtr(p.AllowedGroups)
	return out
}

func apiProviderToConfig(p ProviderOptions) config.ProviderConfig {
	return config.ProviderConfig{
		Id:                   derefStr(p.Id),
		Label:                derefStr(p.Label),
		Issuer:               derefStr(p.Issuer),
		Audience:             derefStr(p.Audience),
		ClientSecret:         derefStr(p.ClientSecret),
		JwksUri:              derefStr(p.JwksUri),
		AllowedHostedDomains: derefStrSlice(p.AllowedHostedDomains),
		AllowedGroups:        derefStrSlice(p.AllowedGroups),
	}
}

func organizationToAPI(o config.OrganizationConfig) OrganizationOptions {
	providers := make([]ProviderOptions, len(o.Providers))
	for i, p := range o.Providers {
		providers[i] = providerConfigToAPI(p)
	}
	out := OrganizationOptions{Providers: &providers}
	if o.DisplayName != "" {
		out.DisplayName = strPtr(o.DisplayName)
	}
	if o.DefaultProviderId != "" {
		out.DefaultProviderId = strPtr(o.DefaultProviderId)
	}
	return out
}

func apiToOrganization(o OrganizationOptions) config.OrganizationConfig {
	var providers []config.ProviderConfig
	if o.Providers != nil {
		providers = make([]config.ProviderConfig, len(*o.Providers))
		for i, p := range *o.Providers {
			providers[i] = apiProviderToConfig(p)
		}
	}
	return config.OrganizationConfig{
		DisplayName:       derefStr(o.DisplayName),
		DefaultProviderId: derefStr(o.DefaultProviderId),
		Providers:         providers,
	}
}

func adminServerInfo(s config.ServerConfig) AdminServerInfo {
	providers := make([]ProviderOptions, len(s.Providers))
	for i, p := range s.Providers {
		providers[i] = providerConfigToAPI(p)
	}
	out := AdminServerInfo{
		Id:                    s.Id,
		Providers:             providers,
		Ready:                 s.IsReady(),
		HasTicketSigningKey:   s.HasTicketSigningKey(),
		HasTransportPublicKey: s.HasTransportPublicKey(),
	}
	if s.TicketSigningKeyEnvironmentVariable != "" {
		out.TicketSigningKeyEnvironmentVariable = strPtr(s.TicketSigningKeyEnvironmentVariable)
	}
	if s.TransportPublicKeyEnvironmentVariable != "" {
		out.TransportPublicKeyEnvironmentVariable = strPtr(s.TransportPublicKeyEnvironmentVariable)
	}
	return out
}

func apiToServerConfig(serverID string, w AdminServerWrite) config.ServerConfig {
	var providers []config.ProviderConfig
	if w.Providers != nil {
		providers = make([]config.ProviderConfig, len(*w.Providers))
		for i, p := range *w.Providers {
			providers[i] = apiProviderToConfig(p)
		}
	}
	return config.ServerConfig{
		Id:                                    serverID,
		TicketSigningKeyEnvironmentVariable:   derefStr(w.TicketSigningKeyEnvironmentVariable),
		TransportPublicKeyEnvironmentVariable: derefStr(w.TransportPublicKeyEnvironmentVariable),
		TicketSigningKey:                      derefStr(w.TicketSigningKey),
		TransportPublicKey:                    derefStr(w.TransportPublicKey),
		Providers:                             providers,
	}
}

func meetingToView(m controlplane.MeetingRecord, origin string) MeetingView {
	return MeetingView{
		Id:              m.Id,
		Title:           m.Title,
		Status:          m.Status,
		StatusDetail:    m.StatusDetail,
		Host:            m.Host,
		Port:            int(m.Port),
		CreatedAt:       m.CreatedAt,
		UpdatedAt:       m.UpdatedAt,
		JoinUrl:         origin + "/join/" + m.InviteToken,
		InvitationReady: strings.EqualFold(m.Status, "ready") && strings.TrimSpace(m.Host) != "",
	}
}

// transportConfig builds the serverTransport object shared by
// ClientConfiguration and JoinManifest, matching TransportConfig in
// Program.cs, including the loopback-only allowUntrustedLoopbackCertificate
// computation.
func transportConfig(origin, serverID, publicKey string) ServerTransportConfig {
	endpoint := origin + "/admission/" + serverID
	loopback := isLoopbackURL(endpoint)
	return ServerTransportConfig{
		ServerPublicKey:                   strPtr(publicKey),
		AdmissionEndpoint:                 strPtr(endpoint),
		AllowUntrustedLoopbackCertificate: &loopback,
	}
}

func isLoopbackURL(raw string) bool {
	u, err := url.Parse(raw)
	if err != nil {
		return false
	}
	host := u.Hostname()
	if host == "localhost" {
		return true
	}
	ip := net.ParseIP(host)
	return ip != nil && ip.IsLoopback()
}

// clientConfiguration builds the canonical client-config JSON shape,
// matching CreateClientConfiguration in Program.cs field-for-field
// (research-sso-broker.md §4.4/§7-3): Audience -> clientId,
// AllowedHostedDomains folded into access.allowedClaims as one {claim:"hd",
// values:[domain]} entry per domain, AllowedGroups -> access.allowedGroups
// directly.
func clientConfiguration(origin, serverID, publicKey string, providers []config.ProviderConfig, defaultProviderID string) ClientConfiguration {
	out := ClientConfiguration{
		ServerTransport: ptrServerTransport(transportConfig(origin, serverID, publicKey)),
		Redirect: &RedirectConfig{
			Mode: strPtr("loopback"),
			Host: strPtr("127.0.0.1"),
			Port: intPtr(0),
			Path: strPtr("/callback"),
		},
		Enforcement: &EnforcementConfig{AllowOfflineWithinTokenValidity: boolPtr(true)},
	}
	if defaultProviderID != "" {
		out.DefaultProviderId = strPtr(defaultProviderID)
	} else if len(providers) > 0 {
		out.DefaultProviderId = strPtr(providers[0].Id)
	}
	clientProviders := make([]ClientProviderConfig, len(providers))
	for i, p := range providers {
		label := p.Label
		if label == "" {
			label = p.Id
		}
		claims := make([]ClaimRule, 0, len(p.AllowedHostedDomains))
		for _, domain := range p.AllowedHostedDomains {
			claims = append(claims, ClaimRule{Claim: strPtr("hd"), Values: &[]string{domain}})
		}
		cp := ClientProviderConfig{
			Id:                strPtr(p.Id),
			Label:             strPtr(label),
			Issuer:            strPtr(p.Issuer),
			ClientId:          strPtr(p.Audience),
			Scopes:            &[]string{"openid", "email", "profile"},
			DisplayNameClaims: &[]string{"name", "preferred_username", "email"},
			Access: &AccessConfig{
				AllowedGroups: strSlicePtr(p.AllowedGroups),
				AllowedClaims: &claims,
			},
		}
		if p.ClientSecret != "" {
			cp.ClientSecret = strPtr(p.ClientSecret)
		}
		clientProviders[i] = cp
	}
	out.Providers = &clientProviders
	return out
}

func ptrServerTransport(t ServerTransportConfig) *ServerTransportConfig { return &t }
func intPtr(i int) *int                                                 { return &i }
func boolPtr(b bool) *bool                                              { return &b }
