import assert from "node:assert/strict";
import { EventEmitter } from "node:events";
import test from "node:test";
import { verifyPropBee } from "./verify-prop-bee.mjs";

const beeUrl = "http://127.0.0.1:4173/BEE/prop.BEE";

function response(status = 206) {
  return {
    request: () => ({ headers: () => ({ range: "bytes=0-7" }) }),
    status: () => status,
    url: () => beeUrl,
  };
}

class FakePage extends EventEmitter {
  constructor() {
    super();
    this.clickCount = 0;
    this.pendingResponse = null;
    this.keyboard = {
      insertText: async () => {},
      press: async () => {},
    };
    this.mouse = {
      down: async () => {},
      move: async () => {},
      up: async () => {
        this.clickCount += 1;
        if (this.clickCount === 5) {
          const beeResponse = response();
          this.emit("response", beeResponse);
          this.pendingResponse?.(beeResponse);
          this.emitConsole("Item key added: prop");
        } else if (this.clickCount === 7) {
          this.emitConsole("Forcefully closing the main menu");
        } else if (this.clickCount === 8) {
          this.emitConsole(`Library provider successfully created item ${beeUrl} with networking: Local`);
        }
      },
    };
  }

  emitConsole(text, type = "log") {
    this.emit("console", { text: () => text, type: () => type });
  }

  async goto() {}

  locator() {
    return { waitFor: async () => {} };
  }

  async screenshot() {}

  async setViewportSize() {}

  async waitForResponse(predicate) {
    return await new Promise((resolve) => {
      this.pendingResponse = (value) => {
        if (predicate(value)) {
          resolve(value);
        }
      };
    });
  }

  async waitForTimeout() {}
}

test("adds and locally spawns a Prop BEE without browser errors", async () => {
  const page = new FakePage();

  const result = await verifyPropBee(page, {
    applicationUrl: "http://127.0.0.1:4173/",
    beeUrl,
    password: "password",
  });

  assert.equal(page.clickCount, 8);
  assert.deepEqual(result.consoleErrors, []);
  assert.equal(result.beeResponses.length, 1);
  assert.equal(result.beeResponses[0].status, 206);
  assert.match(result.spawnLog, /successfully created item/);
});
