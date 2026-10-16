// Exercises the real sandboxed installer window and its IPC against the connected Thor.
const { spawn } = require('node:child_process');
const fs = require('node:fs/promises');
const path = require('node:path');
const assert = require('node:assert/strict');
const root = path.resolve(__dirname, '..');
const port = 19332;
const env = { ...process.env, THOR_INSTALLER_HEADLESS: '1' };
delete env.ELECTRON_RUN_AS_NODE;
const executable = process.env.THOR_INSTALLER_EXECUTABLE || require('electron');
const launchArgs = process.env.THOR_INSTALLER_EXECUTABLE ? [] : ['.'];
const child = spawn(executable, [...launchArgs, `--remote-debugging-port=${port}`],
  { cwd: root, env, windowsHide: true, stdio: 'ignore' });
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
let socket, call;
const deadline = setTimeout(() => { console.error('Installer smoke check timed out'); child.kill(); socket?.close(); process.exitCode = 1; }, 45000);
deadline.unref();
(async () => {
  let target;
  for (let n = 0; n < 300; n++) {
    try { target = (await (await fetch(`http://127.0.0.1:${port}/json`)).json()).find(p => p.type === 'page'); } catch {}
    if (target) break;
    await delay(100);
  }
  assert(target, 'Installer window did not open');
  socket = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((resolve, reject) => { socket.onopen = resolve; socket.onerror = reject; });
  let nextId = 1;
  const waiting = new Map();
  socket.onmessage = event => {
    const message = JSON.parse(event.data);
    if (message.id && waiting.has(message.id)) {
      const { resolve, reject } = waiting.get(message.id);
      waiting.delete(message.id);
      message.error ? reject(message.error) : resolve(message.result);
    }
  };
  call = (method, params = {}) => new Promise((resolve, reject) => {
    const id = nextId++; waiting.set(id, { resolve, reject }); socket.send(JSON.stringify({ id, method, params }));
  });
  let state;
  for (let n = 0; n < 100; n++) {
    const result = await call('Runtime.evaluate', { expression: `JSON.stringify({ title: document.title,
      game: document.getElementById('game').value, apk: document.getElementById('apk').value,
      stage: document.getElementById('stage').textContent, device: document.getElementById('device').textContent })`, returnByValue: true });
    if (!result.result?.value) { await delay(100); continue; }
    state = JSON.parse(result.result.value);
    if (state.game && state.device.includes('Ready')) break;
    await delay(100);
  }
  assert(state.game.endsWith('Command & Conquer Red Alert II'), 'Steam folder was not discovered');
  assert(state.device.includes('AYN Thor') && state.device.includes('Ready'), 'Thor was not discovered');
  assert(state.apk.endsWith('Romanovs-Thor.apk'), 'Local app was not selected');
  await fs.access(state.apk);
  if (process.env.THOR_INSTALLER_EXECUTABLE) assert(state.apk.includes('resources'), 'Packaged app did not select the bundled APK');
  if (process.env.THOR_INSTALLER_VERIFY_INSTALL === '1') {
    // The UI runs the same real transfer/recovery checks as an ordinary user click.
    await call('Runtime.evaluate', { expression: "document.getElementById('install').click()" });
    let finished;
    for (let n = 0; n < 100; n++) {
      const result = await call('Runtime.evaluate', { expression: "JSON.stringify({stage: document.getElementById('stage').textContent, log: document.getElementById('log').textContent})", returnByValue: true });
      finished = JSON.parse(result.result.value);
      if (['Ready to play', 'Install stopped'].includes(finished.stage)) break;
      await delay(100);
    }
    assert.equal(finished.stage, 'Ready to play', finished.log);
    assert(finished.log.includes('6 already correct'), 'Existing retail archives were not verified and retained');
    assert(finished.log.includes('The same APK is already installed'), 'Bundled APK differs from the installed build');
    state.install = finished;
  }
  console.log(JSON.stringify(state, null, 2));
})().catch(error => { console.error(error); process.exitCode = 1; }).finally(async () => {
  if (call && socket?.readyState === WebSocket.OPEN) {
    await Promise.race([call('Browser.close').catch(() => {}), delay(1000)]);
  }
  clearTimeout(deadline); socket?.close(); child.kill();
});
