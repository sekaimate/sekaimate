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
	if p.WebClientId != "" {
		out.WebClientId = strPtr(p.WebClientId)
	}
	if p.WebClientSecret != "" {
		out.WebClientSecret = strPtr(p.WebClientSecret)
	}
	if p.TokenEndpoint != "" {
		out.TokenEndpoint = strPtr(p.TokenEndpoint)
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
		WebClientId:          derefStr(p.WebClientId),
		WebClientSecret:      derefStr(p.WebClientSecret),
		TokenEndpoint:        derefStr(p.TokenEndpoint),
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
	if s.WebSocketUri != "" {
		out.WebSocketUri = strPtr(s.WebSocketUri)
	}
	if s.ServerInfoUri != "" {
		out.ServerInfoUri = strPtr(s.ServerInfoUri)
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
		WebSocketUri:                          derefStr(w.WebSocketUri),
		ServerInfoUri:                         derefStr(w.ServerInfoUri),
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
		WebSocketUri:    strPtrIfNonEmpty(m.WebSocketUri),
		ServerInfoUri:   strPtrIfNonEmpty(m.ServerInfoUri),
	}
}

// transportConfig builds the serverTransport object shared by
// ClientConfiguration and JoinManifest, matching TransportConfig in
// Program.cs, including the loopback-only allowUntrustedLoopbackCertificate
// computation.
func transportConfig(origin, serverID, publicKey, webSocketURI, serverInfoURI string) ServerTransportConfig {
	endpoint := origin + "/admission/" + serverID
	loopback := isLoopbackURL(endpoint)
	out := ServerTransportConfig{
		ServerPublicKey:                   strPtr(publicKey),
		AdmissionEndpoint:                 strPtr(endpoint),
		AllowUntrustedLoopbackCertificate: &loopback,
	}
	if webSocketURI != "" {
		out.WebSocketUri = strPtr(webSocketURI)
	}
	if serverInfoURI != "" {
		out.ServerInfoUri = strPtr(serverInfoURI)
	}
	return out
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
func clientConfiguration(origin, serverID, publicKey, webSocketURI, serverInfoURI string, providers []config.ProviderConfig, defaultProviderID string) ClientConfiguration {
	out := ClientConfiguration{
		ServerTransport: ptrServerTransport(transportConfig(origin, serverID, publicKey, webSocketURI, serverInfoURI)),
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
			ExtraAuthParams: mapPtr(authorizationParameters(p.AllowedHostedDomains)),
		}
		if p.ClientSecret != "" {
			cp.ClientSecret = strPtr(p.ClientSecret)
		}
		clientProviders[i] = cp
	}
	out.Providers = &clientProviders
	return out
}

func authorizationParameters(allowedDomains []string) map[string]string {
	params := map[string]string{"access_type": "offline", "prompt": "consent"}
	clean := make([]string, 0, len(allowedDomains))
	seen := make(map[string]struct{})
	for _, domain := range allowedDomains {
		domain = strings.TrimSpace(domain)
		if domain == "" {
			continue
		}
		key := strings.ToLower(domain)
		if _, ok := seen[key]; ok {
			continue
		}
		seen[key] = struct{}{}
		clean = append(clean, domain)
	}
	if len(clean) == 1 {
		params["hd"] = clean[0]
	} else if len(clean) > 1 {
		params["hd"] = "*"
	}
	return params
}

func mapPtr(value map[string]string) *map[string]string { return &value }

func webClientConfiguration(origin, serverID, publicKey, webSocketURI, serverInfoURI string, providers []config.ProviderConfig, defaultProviderID string) ClientConfiguration {
	out := clientConfiguration(origin, serverID, publicKey, webSocketURI, serverInfoURI, providers, defaultProviderID)
	out.Redirect = &RedirectConfig{Mode: strPtr("browser"), Path: strPtr("/sso-callback")}
	webProviders := make([]ClientProviderConfig, 0, len(providers))
	for _, p := range providers {
		if !p.IsWebConfigured() {
			continue
		}
		label := p.Label
		if label == "" {
			label = p.Id
		}
		claims := make([]ClaimRule, 0, len(p.AllowedHostedDomains))
		for _, domain := range p.AllowedHostedDomains {
			claims = append(claims, ClaimRule{Claim: strPtr("hd"), Values: &[]string{domain}})
		}
		tokenEndpoint := origin + "/web-oidc/" + escapeDataString(serverID) + "/" + escapeDataString(p.Id) + "/token"
		webProviders = append(webProviders, ClientProviderConfig{
			Id: strPtr(p.Id), Label: strPtr(label), Issuer: strPtr(p.Issuer), ClientId: strPtr(p.WebClientId),
			TokenEndpoint: strPtr(tokenEndpoint), Scopes: &[]string{"openid", "email", "profile"},
			ExtraAuthParams:   mapPtr(authorizationParameters(p.AllowedHostedDomains)),
			DisplayNameClaims: &[]string{"name", "preferred_username", "email"},
			Access:            &AccessConfig{AllowedGroups: strSlicePtr(p.AllowedGroups), AllowedClaims: &claims},
		})
	}
	out.Providers = &webProviders
	if len(webProviders) > 0 {
		selected := ""
		for _, provider := range webProviders {
			if provider.Id != nil && *provider.Id == defaultProviderID {
				selected = defaultProviderID
				break
			}
		}
		if selected == "" && webProviders[0].Id != nil {
			selected = *webProviders[0].Id
		}
		out.DefaultProviderId = strPtr(selected)
	}
	return out
}

func ptrServerTransport(t ServerTransportConfig) *ServerTransportConfig { return &t }
func strPtrIfNonEmpty(s string) *string {
	if s == "" {
		return nil
	}
	return &s
}
func intPtr(i int) *int    { return &i }
func boolPtr(b bool) *bool { return &b }
