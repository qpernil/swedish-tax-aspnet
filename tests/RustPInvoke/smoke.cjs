const assert = require('node:assert/strict');
const playwright = require('playwright');

(async () => {
  const engine = process.env.PLAYWRIGHT_BROWSER || 'chromium';
  assert.ok(['chromium', 'firefox', 'webkit'].includes(engine), 'Unsupported browser engine');
  const browser = await playwright[engine].launch({
    headless: true,
    ...(process.env.PLAYWRIGHT_CHANNEL ? { channel: process.env.PLAYWRIGHT_CHANNEL } : {}),
  });
  try {
    const page = await browser.newPage();
    const errors = [];
    const requests = [];
    page.on('pageerror', error => errors.push(error.message));
    page.on('request', request => requests.push(request.url()));
    await page.goto(process.env.PINVOKE_URL || 'http://127.0.0.1:5182');
    await page.locator('#verification').waitFor({ timeout: 60000 });
    assert.equal(await page.getByRole('status').innerText(), 'Rust P/Invoke succeeded');
    const expected = /^Passed all 10 FFI functions: 229 C ABI layout checks; 1182 monthly, 336 annual, 112 mixed-income and 120 plan\/dividend\/support reference cases; 1000 allocation\/free cycles; null and invalid inputs\.$/;
    assert.match(await page.locator('#verification').innerText(), expected);
    assert.match(await page.locator('#result').innerText(), /Value: 12951\nCalls: 1/);

    // Once loaded, the interop and reference checks work without a server.
    await page.context().setOffline(true);
    for (let i = 0; i < 10; i++) await page.getByRole('button', { name: 'Call Rust again', exact: true }).click();
    await page.getByRole('button', { name: 'Run reference checks again', exact: true }).click();
    assert.match(await page.locator('#result').innerText(), /Value: 12951\nCalls: 11/);
    assert.match(await page.locator('#verification').innerText(), expected);
    assert.equal(requests.filter(url => /\/dotnet\.native\.[^/]*wasm$/.test(url)).length, 1);
    assert.equal(requests.some(url => /swedish_tax_web|bridge\.js|\/rust\//.test(url)), false);
    assert.deepEqual(errors, []);
    if (process.env.PINVOKE_SCREENSHOT) await page.screenshot({ path: process.env.PINVOKE_SCREENSHOT, fullPage: true });
    console.log(`PASS (${engine} ${browser.version()}): all 10 FFI functions, 229 ABI layout checks, 1,750 native reference cases, 1,000 allocation/free cycles, repeated offline calls, one linked native WASM module.`);
  } finally {
    await browser.close();
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
