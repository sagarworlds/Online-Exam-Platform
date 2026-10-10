# Step-by-step: host the Exam Platform on Render with a Neon database

**What you end up with**

| Part | Runs on | Cost |
|---|---|---|
| Website (Angular) | Render static site | Free |
| API (.NET) | Render web service (Docker) | Free |
| Database (Postgres) | Neon | Free |

Both services deploy from `main` (`branch: main` in `render.yaml`): merging a pull request redeploys them automatically.

**Free-plan limits**

- The Render API sleeps after about 15 minutes without traffic; the first request afterwards takes up to a minute.
- The Neon database sleeps after 5 minutes idle and wakes in about a second. It holds 0.5 GB.
- The API can only e-mail sign-in codes. It cannot send SMS yet, because no SMS provider is built in, so candidates and staff must use e-mail. `Sms__Enabled` in `render.yaml` is the master switch for SMS: off unless set to `true`. Turning it on before a provider exists still sends nothing, and each attempt is logged.

**What you need before starting**

- A GitHub account with this repository, a Render account (render.com) and a Neon account (neon.tech); all can sign in with GitHub.
- Docker Desktop running on your machine, with the Postgres container that holds your data.
- A free Brevo account (brevo.com) to e-mail sign-in codes. Render's free plan blocks outbound SMTP, so mail is sent through Brevo's HTTPS API; see step 3.

---

## Step 1. Create the Neon database

1. Sign in to neon.tech and choose **Create project**. Name it `examplatform`, pick Postgres 16 or newer and the region nearest your users.
2. Open the project's **Dashboard** and choose **Connect**.
3. Turn **Connection pooling** off. The host must not contain `-pooler`.
4. Note the **host**, **database**, **role** and **password** (reveal the password).

Build two strings from them. Keep both somewhere private:

```
Npgsql string (for Render):
Host=<host>;Port=5432;Database=<database>;Username=<role>;Password=<password>;SSL Mode=Require;Trust Server Certificate=true

URL (for the data copy):
postgresql://<role>:<password>@<host>/<database>?sslmode=require
```

## Step 2. Copy your existing data into Neon

Run these in PowerShell. They can run from any folder.

1. Find your database container:
   ```powershell
   docker ps --format "{{.Names}}  {{.Image}}"
   ```
   Use the name on the `postgres:16` row as `<name>` below. If nothing is listed, open the project folder and run `docker compose up -d postgres` first.
2. Make a dump inside the container (do not use `>`, PowerShell corrupts binary output):
   ```powershell
   docker exec <name> pg_dump -U examplatform -d examplatform --no-owner --no-privileges -Fc -f /tmp/examplatform.dump
   ```
3. Keep a backup copy on your machine:
   ```powershell
   docker cp <name>:/tmp/examplatform.dump $HOME\examplatform.dump
   ```
4. Restore into Neon using the URL from step 1:
   ```powershell
   docker exec <name> pg_restore --no-owner --no-privileges --dbname "<the URL>" /tmp/examplatform.dump
   ```
   Warnings about extensions or `public` schema can be ignored; errors mentioning `already exists` mean the database was not empty (see Troubleshooting).
5. Check the copy by comparing counts. Both numbers must match:
   ```powershell
   docker exec <name> psql -U examplatform -d examplatform -c "select count(*) from identity.\"Users\";"
   docker exec <name> psql "<the URL>" -c "select count(*) from identity.\"Users\";"
   ```

Your local database is not changed. Do not run `docker compose down -v` (it deletes the data volume) until you are sure the copy is complete. The dump holds real candidate data and password hashes: keep it out of git and delete it when you no longer need it.

## Step 3. Create a Brevo API key

1. In Brevo open **SMTP & API > API Keys** (not SMTP & API > SMTP — that page's "SMTP key" is for a direct SMTP connection, which Render's free plan blocks).
2. Choose **Generate a new API key**, name it (e.g. `exam-platform-render`) and copy it. It is shown only once.
3. Open **Senders, Domains & Dedicated IPs > Senders**, add the address you will send from, and confirm it through the e-mail Brevo sends. Brevo refuses mail from an unverified sender.

The free plan allows about 300 e-mails a day, which covers sign-in codes for a small cohort.

## Step 4. Create the services on Render

1. In Render choose **New > Blueprint**, pick this repository and apply `render.yaml`. Render creates both services and starts a first deploy, which fails until step 5 and 6 are done — that's expected.
2. **Note each service's real address now**, from the top of its page (e.g. `exam-platform-api`, `exam-platform-web`). Render appends a random suffix whenever the plain name is already taken by someone else's service, so the address is often `https://exam-platform-api-<random>.onrender.com`, not the bare `https://exam-platform-api.onrender.com`. Copy both addresses exactly; you need them in the next two steps.

## Step 5. Fill in the API settings

On `exam-platform-api` > **Environment**, fill in every value marked `sync: false`:

| Key | Value |
|---|---|
| `ConnectionStrings__Postgres` | The Npgsql string from step 1. |
| `Cors__AllowedOrigins__0` | The **website's** real address from step 4 (no trailing slash). Must match exactly, or the browser refuses every API call with a CORS error. |
| `Brevo__ApiKey` | The API key from step 3. |
| `Brevo__SenderEmail` | The verified sender address from step 3. |
| `Invite__LinkBaseUrl` | The website's real address again, so invitation links open the site. |

`Jwt__SigningKey` is generated for you. Everyone has to sign in again after it changes.

## Step 6. Point the website at the API

1. On `exam-platform-web` > **Environment**, set `API_BASE_URL` to the **API's** real address from step 4.
2. Choose **Manual Deploy > Clear build cache & deploy**. The address is baked into the JavaScript bundle when the site is built, so any change needs a rebuild — saving the setting alone does not take effect.

## Step 7. Check that it works

1. Open `<the API's real address>/v1/health` directly in a browser tab. It should answer `Healthy`. After a sleep this can take a minute. If it 404s or times out, the service failed to start — check its **Logs** before going further.
2. In the API's **Logs**, look for `Migrated and seeded <Module>` once per module.
3. Open the website and sign in as a candidate with an e-mail address that exists in your data. The code arrives by e-mail.
4. Sign in as the administrator (password, then an e-mailed code). The administrator's address must be one you can read.
5. Open an exam you created earlier to confirm the data came across.

## Step 8 (optional). Send phone sign-in codes on WhatsApp

Without this, a candidate who signs in with a phone number gets no code (e-mail works as in step 7). This step uses Meta's WhatsApp Cloud API, and most of it is on Meta's side. The README's [WhatsApp (Meta Cloud API)](../README.md#whatsapp-meta-cloud-api) section is the full checklist; the Render part is:

1. In Meta's app dashboard and WhatsApp Manager, get the values in the README's list: the **permanent access token**, the **phone number ID**, the **app secret**, and the name of an **approved Authentication template**. Choose a **verify token** yourself (any long random string).
2. On `exam-platform-api` > **Environment**, leave `WhatsApp__Enabled` unset for now (WhatsApp is **off** unless it is `true`: nothing is sent, whatever else is set) and fill in `WhatsApp__AccessToken`, `WhatsApp__PhoneNumberId`, `WhatsApp__OtpTemplateName`, `WhatsApp__AppSecret` and `WhatsApp__WebhookVerifyToken`. If the template is not in English, also add `WhatsApp__OtpTemplateLanguage` (for example `hi`) to match it.
3. **Only after those are saved**, set `Identity__OtpDelivery__PhoneProvider` to `WhatsApp`, then `WhatsApp__Enabled` to `true`. Turning the switch on checks the settings: if any of the first three is missing the API refuses to start and its log names the missing setting, which is the point, better than a failed sign-in. To stop all WhatsApp messages at once later, set `WhatsApp__Enabled` back to `false` (or delete it); the API keeps starting.
4. In Meta's dashboard (WhatsApp > Configuration) set the webhook **Callback URL** to `<the API's real address>/v1/webhooks/whatsapp` and the **Verify token** to your `WhatsApp__WebhookVerifyToken`, choose **Verify and save**, and subscribe to `messages`. Also set the app's **Privacy Policy URL** to `<the website's real address>/privacy.html`.
5. Sign in on the website with a phone number that has an account (with a test number, one you verified in Meta's dashboard). The code arrives on WhatsApp. In the API's **Logs** you should see `was handed to WhatsApp as message`, then `is delivered`.

**Also send invitations' exam codes on WhatsApp (optional, after the above works).** Create a **Utility** template with the body in the README's [Invitations on WhatsApp](../README.md#invitations-on-whatsapp), wait for Meta to approve it, then set `Invite__WhatsApp__TemplateName` on `exam-platform-api` (WhatsApp itself must be switched on, `WhatsApp__Enabled` = `true`, or nothing is sent) (and `Invite__WhatsApp__TemplateLanguage` if it is not English). From then on, inviting an e-mail address that belongs to an account with a phone number also sends the code to that phone. Read the README's note on consent first: this is the operator's confirmation that those people agreed to WhatsApp notices.

## Step 9 (optional). Send exam reminders and result e-mails on time

The platform e-mails its candidates when an exam is about to start (once within a day, once within the hour), when their result can be seen, and when a score they saw is revised. This needs working e-mail (step 3): with none, nothing is sent and nothing is lost, and the e-mails that are due go out once e-mail works.

The API has a timer that sends these every five minutes, **but a free Render service sleeps when it has had no traffic for about 15 minutes**, and the hours before an exam are usually quiet. So a GitHub workflow wakes the API and starts a pass every ten minutes:

1. Make up a long random key (at least 24 characters). It is the password for starting a pass; nothing else can use it.
2. On `exam-platform-api` > **Environment**, set `Notifications__RunKey` to that key and save. Until it is set, the route answers 404 as if it were not there.
3. In the GitHub repository, open **Settings** > **Secrets and variables** > **Actions** and add two repository secrets: `NOTIFICATIONS_RUN_KEY` (the same key) and `NOTIFICATIONS_API_URL` (the API's real address, like `https://exam-platform-api-xxxx.onrender.com`). Until both are set the workflow does nothing.
4. Open **Actions** > **Scheduled notifications** > **Run workflow** once to try it. The run's log ends with a line like `{"mailAvailable":true,"remindersSent":0,...}`. `"mailAvailable":false` means the API has no e-mail configured.

Good to know:

- A pass sends only what is due and writes down what it sent, so an extra, late or overlapping pass sends nothing twice. The API's own timer and the workflow can both run.
- GitHub runs scheduled workflows on a best-effort basis and can be several minutes late, so the "within the hour" reminder may arrive a little later than that.
- A result or revision more than a day old is never announced, so switching this on does not e-mail old results.
- GitHub turns scheduled workflows off after 60 days without repository activity. If reminders stop, open **Actions** and enable the workflow again.
- To stop all of it, delete `NOTIFICATIONS_RUN_KEY` and set `Notifications__Enabled` to `false` on the API.

## Updating later

- Merging a pull request into `main` redeploys both services automatically.
- Database changes are applied by the API on start. The data is not lost.

## Keeping keys out of GitHub

No real key is ever written to a file in the repository. The secrets live only in the Render and Neon dashboards:

- `render.yaml` lists the secret settings with `sync: false`. That means "ask for the value in the Render dashboard"; the value is stored by Render, never in git.
- The Neon password, the Brevo API key, the WhatsApp access token, app secret and verify token, and the JWT signing key (generated by Render) are typed into **Environment** on Render. Do not paste them into code, docs, commit messages, issues or PR comments.
- `appsettings.Development.json` holds only throw-away local values (the Docker database password and a development signing key). Production ignores that file.
- Never commit the database dump, a `.env` file or a screenshot of the Environment page. `.env` is already in `.gitignore`.

If a key is ever pasted somewhere public, treat it as leaked: generate a new Brevo API key, reset the Neon role password, and update the value on Render. Deleting the commit is not enough, because GitHub keeps history.

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| API log: `Jwt:SigningKey` or connection errors | A setting from step 5 is missing or mistyped. Check for spaces and the exact key names (double underscores). |
| API log: `password authentication failed` or SSL errors | Use the non-pooler host and keep `SSL Mode=Require;Trust Server Certificate=true`. |
| Browser shows a CORS error, or `/v1/health` 404s from the browser | Usually both at once, and usually the same cause: `API_BASE_URL` (on the website) or `Cors__AllowedOrigins__0` (on the API) still has the guessed address without Render's random suffix. Open each service's page, copy its real address, and set both again exactly — see step 4. |
| Website calls `localhost` or the wrong API | `API_BASE_URL` was wrong when the site was built. Fix it and redeploy with a cleared cache (step 6). |
| No sign-in e-mail arrives | Check the API log for `Brevo refused a message` or `did not respond in time`; verify `Brevo__ApiKey` is an **API key** (not an SMTP key) and `Brevo__SenderEmail` is verified in Brevo. Then check Brevo's **Transactional > Email Activity** log for the message's status. Check spam. |
| API will not start and the log says `Identity:OtpDelivery:PhoneProvider is 'WhatsApp' and WhatsApp is switched on, but WhatsApp:... not set` | Fill in the named `WhatsApp__...` settings (step 8), or set `WhatsApp__Enabled` back to `false`, or clear `Identity__OtpDelivery__PhoneProvider`. |
| API will not start and the log says `Invite:WhatsApp:TemplateName is set and WhatsApp is switched on, but WhatsApp:... not set` | WhatsApp is switched on (`WhatsApp__Enabled` is `true`) and an invitation template is named, but the settings named in the message are missing. Fill them in (step 8), or set `WhatsApp__Enabled` back to `false`, or clear `Invite__WhatsApp__TemplateName`. |
| An invitation's code does not arrive on WhatsApp | It is only sent when `WhatsApp__Enabled` is `true`, `Invite__WhatsApp__TemplateName` is set, and the invited e-mail address has an **active account with a phone number** (no account, no phone, or an unverified account get nothing; with the switch off the log says `WhatsApp message not sent: WhatsApp is switched off`, or nothing if no number was even looked up). The log shows `was handed to WhatsApp for ********10 as message` when it was sent, `not a phone number WhatsApp can reach` for a number it could not dial, and otherwise `WhatsApp refused a message` with Meta's reason (usually the template: not approved, or approved in another language than `Invite__WhatsApp__TemplateLanguage`, or not taking four values). |
| No WhatsApp code arrives | In the API log: `WhatsApp message not sent: WhatsApp is switched off` means `WhatsApp__Enabled` is not `true`. `WhatsApp refused a message` names Meta's error (an expired or wrong token, a template that is not approved or is in another language than `WhatsApp__OtpTemplateLanguage`, or, with a test number, a recipient not on its list). `was handed to WhatsApp` followed by `... failed: 131026` means the number is not on WhatsApp. No `handed` line at all: look for `not a phone number WhatsApp can reach`, and check the number carries or is given a country code (`WhatsApp__DefaultCountryCode`). |
| Meta says the webhook could not be verified | The callback URL must be the **API's** address, not the website's, ending `/v1/webhooks/whatsapp`; the verify token must equal `WhatsApp__WebhookVerifyToken` exactly; and both `WhatsApp__AppSecret` and the verify token must be set (until they are, the route answers 404). |
| `pg_restore` says `already exists` | The API started before the restore. Run it again with `--clean --if-exists` added. |
| Everyone shares one rate limit | Render's proxy range differs. Adjust `ForwardedHeaders__KnownNetworks__0/1` to the range shown in the API log. |
