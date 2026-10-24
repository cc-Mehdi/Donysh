import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { runInNewContext } from "node:vm";
import test from "node:test";

test("password rules update as the user types and visibility can be toggled", () => {
    const items = Object.fromEntries(["length", "letter", "digit"].map((name) => {
        const classes = new Set();
        return [name, { classList: { toggle: (value, enabled) => enabled ? classes.add(value) : classes.delete(value), contains: (value) => classes.has(value) } }];
    }));
    const rules = { querySelector: (selector) => items[selector.match(/"(.*?)"/)?.[1]] };
    const listeners = {};
    class FakeInput {
        value = "";
        type = "password";
        closest = () => ({ querySelector: () => rules });
        addEventListener = (name, handler) => { listeners[name] = handler; };
    }
    const input = new FakeInput();
    const button = {
        textContent: "نمایش",
        getAttribute: () => "password",
        setAttribute: () => {},
        querySelector: () => null,
        addEventListener: (name, handler) => { listeners[`button-${name}`] = handler; }
    };
    const script = readFileSync(new URL("../wwwroot/js/password-toggle.js", import.meta.url), "utf8");
    runInNewContext(script, {
        HTMLInputElement: FakeInput,
        document: {
            getElementById: () => input,
            querySelectorAll: (selector) => selector === "[data-password-toggle]" ? [button] : [input]
        }
    });

    assert.ok(Object.values(items).every((item) => !item.classList.contains("is-met")));
    input.value = "abc12345";
    listeners.input();
    assert.ok(Object.values(items).every((item) => item.classList.contains("is-met")));
    input.value = "abcdefgh";
    listeners.input();
    assert.equal(items.digit.classList.contains("is-met"), false);
    listeners["button-click"]();
    assert.equal(input.type, "text");
    assert.equal(button.textContent, "پنهان");
});
