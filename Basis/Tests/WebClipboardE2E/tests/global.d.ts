interface BasisClipboardE2EResult {
  operation: 'write' | 'read';
  succeeded: boolean;
  text: string;
  error: string;
}

interface BasisClipboardE2EApi {
  ready: boolean;
  secureContext: boolean;
  clipboardAvailable: boolean;
  results: BasisClipboardE2EResult[];
  setWriteText(text: string): void;
}

interface Window {
  basisClipboardE2E?: BasisClipboardE2EApi;
}
