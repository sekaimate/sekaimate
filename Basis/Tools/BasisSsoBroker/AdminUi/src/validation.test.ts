import { describe, expect, it } from "vite-plus/test";
import { validateBrowserEndpoints } from "./validation";

describe("validateBrowserEndpoints", () => {
  it("allows both values to be empty for native-only servers", () => {
    expect(validateBrowserEndpoints("", "")).toBeNull();
  });

  it("requires the pair to be complete", () => {
    expect(validateBrowserEndpoints("wss://room.example/basis", "")).toMatch(/両方/);
  });

  it("requires secure schemes for remote endpoints", () => {
    expect(validateBrowserEndpoints("ws://room.example/basis", "http://room.example/info")).toMatch(/wss/);
    expect(validateBrowserEndpoints("wss://room.example/basis", "http://room.example/info")).toMatch(/https/);
  });

  it("allows loopback development endpoints", () => {
    expect(validateBrowserEndpoints("ws://localhost/basis", "http://localhost/info")).toBeNull();
  });
});
