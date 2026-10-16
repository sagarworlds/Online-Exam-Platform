// Starts the real API for the full-stack browser tests and keeps it running until Playwright stops it.
//
// The API runs the way a developer runs it (the Development environment: it migrates and seeds its own database, and writes each sign-in
// code to its log instead of sending it), against the Postgres named by E2E_POSTGRES, or the one the Development settings name, which is
// the database CI starts. What it writes goes to e2e/.run/api.log, and e2e/.run/settings.json says who the administrator is, so the tests
// can sign in and read the codes.
//
// The administrator is new on every run, with a password made for the run: the API never changes an administrator that already exists, so
// reusing one would lock a second run out of a database that kept the first run's password.
import { spawn } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { existsSync, mkdirSync, openSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const webRoot = resolve(fileURLToPath(new URL('../..', import.meta.url)));
const dll = process.env.E2E_API_DLL ?? resolve(webRoot, '../api/src/Host/ExamPlatform.Api/bin/Release/net10.0/ExamPlatform.Api.dll');
const apiUrl = process.env.E2E_API_URL ?? 'http://127.0.0.1:5080';

if (!existsSync(dll)) {
  console.error(`No API build at ${dll}. Run "dotnet build src/Host/ExamPlatform.Api -c Release" in apps/api first.`);
  process.exit(1);
}

const runDir = join(webRoot, 'e2e', '.run');
mkdirSync(runDir, { recursive: true });
const apiLog = join(runDir, 'api.log');
const logFd = openSync(apiLog, 'w');

const suffix = randomBytes(6).toString('hex');
const adminEmail = `staff-${suffix}@e2e.test`;
// Long and random, with every kind of character the password policy asks for, and nothing of the address in it.
const adminPassword = `Zq-${randomBytes(12).toString('hex')}-9Kd!`;
writeFileSync(join(runDir, 'settings.json'), JSON.stringify({ adminEmail, adminPassword, apiUrl, apiLog }));

// The limits are per client address, and every request here comes from the same one, far more than a real person would send.
const manyRequests = '100000';

const env = {
  ...process.env,
  ASPNETCORE_ENVIRONMENT: 'Development',
  ASPNETCORE_URLS: apiUrl,
  Identity__Bootstrap__AdminEmail: adminEmail,
  Identity__Bootstrap__AdminPassword: adminPassword,
  RateLimiting__Global__PermitLimit: manyRequests,
  Identity__RateLimits__OtpRequest__PermitLimit: manyRequests,
  Identity__RateLimits__OtpVerify__PermitLimit: manyRequests,
  Identity__RateLimits__PasswordLogin__PermitLimit: manyRequests,
  Identity__RateLimits__PasswordReset__PermitLimit: manyRequests,
  // The timer that sends reminders in the background would send mail in the middle of a test.
  Notifications__Enabled: 'false',
};
if (process.env.E2E_POSTGRES) {
  env.ConnectionStrings__Postgres = process.env.E2E_POSTGRES;
}

const api = spawn('dotnet', [dll], { cwd: dirname(dll), env, stdio: ['ignore', logFd, logFd] });
api.on('exit', (code, signal) => process.exit(signal ? 0 : (code ?? 1)));

for (const signal of ['SIGINT', 'SIGTERM']) {
  process.on(signal, () => api.kill(signal));
}

console.log(`API starting at ${apiUrl}, logging to ${apiLog}`);
