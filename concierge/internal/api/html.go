package api

import (
	"bytes"
	"html/template"
)

// pageStyle is the shared inline stylesheet used by every HTML page below,
// matching the C# broker's pages (Program.cs) closely enough to keep the
// operator-facing look consistent; it is not part of any wire-compatibility
// contract (research-sso-broker.md §7 only pins the loopback-bridge URLs,
// query parameter names, and the postMessage contract, not page styling).
const pageStyle = `body{margin:0;background:#f5f7fa;color:#202332;font:16px system-ui,-apple-system,sans-serif}.page{max-width:44rem;margin:0 auto;padding:4rem 1.25rem}.card{background:#fff;border:1px solid #d5dbdb;border-radius:16px;padding:2rem;box-shadow:0 4px 16px #172b4d12}.eyebrow{color:#553bc0;font-size:.8rem;font-weight:800;letter-spacing:.12em}h1{margin:.5rem 0 1rem;font-size:2rem}p{line-height:1.65}.button{display:inline-block;margin:1rem 0;padding:.8rem 1.2rem;border-radius:8px;background:#553bc0;color:#fff;text-decoration:none;font-weight:700}.hint{color:#5f6b7a;font-size:.9rem}iframe{display:none}`

type enrollLandingData struct {
	Callback string
}

var enrollLandingTemplate = template.Must(template.New("enrollLanding").Parse(`<!doctype html><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>SekaiMate — Basis 設定</title>
<style>` + pageStyle + `</style>
<main class="page"><section class="card"><div class="eyebrow">SEKAIMATE</div><h1>組織設定を Basis に適用</h1><p>Basis を起動した状態で、下のボタンを押してください。ログインに必要な組織設定をこの端末の Basis に送ります。</p>
<p><a class="button" href="{{.Callback}}">Basis に設定を送る</a></p>
<p class="hint">設定 URL は 10 分間・一回限りです。送信後は Basis に戻ってログインしてください。</p></section></main>
`))

func renderEnrollLanding(callback string) (string, error) {
	var buf bytes.Buffer
	if err := enrollLandingTemplate.Execute(&buf, enrollLandingData{Callback: callback}); err != nil {
		return "", err
	}
	return buf.String(), nil
}

type joinPageData struct {
	Title       string
	DeepLink    template.URL
	LoopbackURL string
}

// joinPageTemplate reproduces the loopback-bridge contract described in
// research-sso-broker.md §7-7: a hidden iframe pointed at
// http://127.0.0.1:56831/basis-join (fixed port/path/query names), a
// postMessage('basis-join-received') listener, and a 900ms fallback to the
// basisdemo:// deep link if no message arrives.
var joinPageTemplate = template.Must(template.New("joinPage").Parse(`<!doctype html><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>{{.Title}} — SekaiMate</title><style>` + pageStyle + `</style>
<main class="page"><section class="card"><div class="eyebrow">SEKAIMATE</div><h1>{{.Title}}</h1><p id="status">Basis を開いて会議への参加を準備しています…</p><p><a class="button" href="{{.DeepLink}}">Basis で参加する</a></p><p class="hint">自動で開かない場合は、上のボタンを押してください。</p><iframe id="basis-join" src="{{.LoopbackURL}}" title="Basis join bridge"></iframe></section></main>
<script>let received=false;addEventListener('message',e=>e.data==='basis-join-received'&&(received=true,document.querySelector('#status').textContent='Basis に会議への参加を渡しました。Basis に戻ってください。'));setTimeout(()=>!received&&(location.href={{.DeepLink}}),900);</script>
`))

func renderJoinPage(title, deepLink, loopbackURL string) (string, error) {
	var buf bytes.Buffer
	if err := joinPageTemplate.Execute(&buf, joinPageData{Title: title, DeepLink: template.URL(deepLink), LoopbackURL: loopbackURL}); err != nil {
		return "", err
	}
	return buf.String(), nil
}

type joinOpenPageData struct {
	Title    string
	DeepLink template.URL
}

var joinOpenPageTemplate = template.Must(template.New("joinOpenPage").Parse(`<!doctype html><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>{{.Title}} — SekaiMate</title><style>` + pageStyle + `</style>
<main class="page"><section class="card"><div class="eyebrow">SEKAIMATE</div><h1>{{.Title}}</h1><p>Basis を起動済みなら、下のボタンを押すとこの会議室へ接続します。</p><p><a class="button" href="{{.DeepLink}}">Basis で参加する</a></p><p class="hint">通常の招待 URL は自動的にこの操作を行います。この画面はブラウザのフォールバック用です。</p></section></main>
`))

func renderJoinOpenPage(title, deepLink string) (string, error) {
	var buf bytes.Buffer
	if err := joinOpenPageTemplate.Execute(&buf, joinOpenPageData{Title: title, DeepLink: template.URL(deepLink)}); err != nil {
		return "", err
	}
	return buf.String(), nil
}
