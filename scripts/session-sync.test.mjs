import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import vm from "node:vm";

const scripts = [
    "HesabYar.Web/wwwroot/js/session-sync.js",
    "Donysh.Life/wwwroot/js/session-sync.js"
];

const flushPromises = async () => {
    await Promise.resolve();
    await Promise.resolve();
};

const createHarness = async (path, initialStatus = 401) => {
    const listeners = new Map();
    const formListeners = new Map();
    const timeouts = [];
    const redirects = [];
    let status = initialStatus;
    let fetchCount = 0;
    let pendingFetch = null;

    const document = {
        currentScript: {
            dataset: {
                sessionStatusUrl: "/Account/SessionStatus",
                signedOutUrl: "/Account/SignedOut"
            }
        },
        visibilityState: "visible",
        querySelectorAll: selector => selector === "form[data-logout-form]"
            ? [{ addEventListener: (name, callback) => formListeners.set(name, callback) }]
            : [],
        addEventListener: (name, callback) => listeners.set(`document:${name}`, callback)
    };

    const window = {
        location: { replace: url => redirects.push(url) },
        addEventListener: (name, callback) => listeners.set(`window:${name}`, callback),
        setInterval: () => 1,
        setTimeout: callback => {
            timeouts.push(callback);
            return timeouts.length;
        },
        clearTimeout: () => {}
    };

    const fetch = async () => {
        fetchCount += 1;
        if (pendingFetch) return pendingFetch;
        return { status };
    };

    vm.runInNewContext(await readFile(path, "utf8"), { document, window, fetch });

    return {
        formListeners,
        listeners,
        redirects,
        timeouts,
        fetchCount: () => fetchCount,
        setStatus: value => { status = value; },
        setPendingFetch: promise => { pendingFetch = promise; }
    };
};

for (const path of scripts) {
    test(`${path}: a stale visible tab redirects after a 401`, async () => {
        const harness = await createHarness(path);
        harness.listeners.get("window:focus")();
        await flushPromises();
        assert.deepEqual(harness.redirects, ["/Account/SignedOut"]);
    });

    test(`${path}: an in-flight check cannot interrupt the logout navigation`, async () => {
        const harness = await createHarness(path);
        let resolveFetch;
        harness.setPendingFetch(new Promise(resolve => { resolveFetch = resolve; }));
        harness.listeners.get("window:focus")();
        harness.formListeners.get("submit")();
        resolveFetch({ status: 401 });
        await flushPromises();
        assert.deepEqual(harness.redirects, []);
    });

    test(`${path}: a stranded logout page recovers without a manual refresh`, async () => {
        const harness = await createHarness(path);
        harness.formListeners.get("submit")();
        assert.equal(harness.timeouts.length, 1);
        harness.timeouts[0]();
        await flushPromises();
        assert.deepEqual(harness.redirects, ["/Account/SignedOut"]);
    });

    test(`${path}: a bfcache restore resumes session checks`, async () => {
        const harness = await createHarness(path);
        harness.formListeners.get("submit")();
        harness.listeners.get("window:pageshow")({ persisted: true });
        await flushPromises();
        assert.deepEqual(harness.redirects, ["/Account/SignedOut"]);
    });

    test(`${path}: a still-authenticated session is not redirected`, async () => {
        const harness = await createHarness(path, 204);
        harness.formListeners.get("submit")();
        harness.timeouts[0]();
        await flushPromises();
        assert.deepEqual(harness.redirects, []);
        assert.equal(harness.fetchCount(), 1);
    });
}
