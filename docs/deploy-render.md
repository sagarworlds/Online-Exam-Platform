# Step-by-step: host the Exam Platform on Render with a Neon database

**What you end up with**

| Part | Runs on | Cost |
|---|---|---|
| Website (Angular) | Render static site | Free |
| API (.NET) | Render web service (Docker) | Free |
| Database (Postgres) | Neon | Free |

**About this branch.** Everything Render needs lives on the branch `SP/feature/render-deployment` and Render deploys from that branch (`branch:` is set in `render.yaml`). It is kept separate from `main` on purpose: do not merge it, and keep it up to date with `main` only when you want new features deployed.

**Free-plan limits**

- The Render API sleeps after about 15 minutes without traffic; the first request afterwards takes up to a minute.
- The Neon database sleeps after 5 minutes idle and wakes in about a second. It holds 0.5 GB.
- The API can only e-mail sign-in codes. SMS is not supported yet, so candidates and staff must use e-mail.

**What you need before starting**

- A GitHub account with this repository, a Render account (render.com) and a Neon account (neon.tech); all can sign in with GitHub.
- Docker Desktop running on your machine, with the Postgres container that holds your data.
- A mail account the API can send from. A free Brevo account (brevo.com, SMTP & API) works; so does Gmail with an app password. You need its SMTP host, port, user name and password.

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

## Step 3. Create the services on Render

1. In Render choose **New > Blueprint** and connect your GitHub repository.
2. Choose the branch **SP/feature/render-deployment**. Render reads `render.yaml` and lists two services: `exam-platform-api` and `exam-platform-web`.
3. Render asks for the values marked secret. You can leave them empty now and fill them in during step 4. Choose **Apply**.
4. The first deploy of the API will fail or stay unhealthy until step 4 is done. That is expected.

## Step 4. Fill in the API settings

Open `exam-platform-api` > **Environment** and set:

| Key | Value |
|---|---|
| `ConnectionStrings__Postgres` | The Npgsql string from step 1. |
| `Cors__AllowedOrigins__0` | The website's address, e.g. `https://exam-platform-web.onrender.com`. No trailing slash. You can read it on the `exam-platform-web` page. |
| `Invite__LinkBaseUrl` | The same website address. |
| `Smtp__Host` | Your mail server host, e.g. `smtp-relay.brevo.com`. |
| `Smtp__User` | Your SMTP user name. |
| `Smtp__Password` | Your SMTP password or key. |
| `Smtp__From` | The sender address, one your mail account allows. |

Already set for you: `ASPNETCORE_ENVIRONMENT=Production`, `Identity__OtpDelivery__Provider=Smtp`, `Smtp__Port=587`, `Smtp__EnableSsl=true`, `Database__MigrateAndSeedOnStartup=true`, the proxy ranges, and a generated `Jwt__SigningKey`. Save the changes; Render redeploys the API.

## Step 5. Point the website at the API

1. Open `exam-platform-api` and copy its address, e.g. `https://exam-platform-api.onrender.com`.
2. Open `exam-platform-web` > **Environment**. If `API_BASE_URL` differs from that address, change it.
3. Choose **Manual Deploy > Clear build cache & deploy**. The address is baked in when the site is built, so any change needs a rebuild.

## Step 6. Check that it works

1. Open `https://<api address>/v1/health`. It should answer `Healthy`. After a sleep this can take a minute.
2. In the API's **Logs**, look for `Migrated and seeded <Module>` once per module.
3. Open the website and sign in as a candidate with an e-mail address that exists in your data. The code arrives by e-mail.
4. Sign in as the administrator (password, then an e-mailed code). The administrator's address must be one you can read.
5. Open an exam you created earlier to confirm the data came across.

## Updating later

- Push to `SP/feature/render-deployment`: Render redeploys both services.
- To bring in new features from `main`, merge `main` into this branch (never the other way round), wait for the checks to pass, then push.
- Database changes are applied by the API on start. The data is not lost.

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| API log: `Jwt:SigningKey` or connection errors | A setting from step 4 is missing or mistyped. Check for spaces and the exact key names (double underscores). |
| API log: `password authentication failed` or SSL errors | Use the non-pooler host and keep `SSL Mode=Require;Trust Server Certificate=true`. |
| Browser shows a CORS error | `Cors__AllowedOrigins__0` must be the exact website address, without a trailing slash. |
| Website calls `localhost` or the wrong API | `API_BASE_URL` was wrong when the site was built. Fix it and redeploy with a cleared cache. |
| No sign-in e-mail arrives | Check the API log for `could not be delivered`; verify the SMTP values and that `Smtp__From` is an allowed sender. Check spam. |
| `pg_restore` says `already exists` | The API started before the restore. Run it again with `--clean --if-exists` added. |
| Everyone shares one rate limit | Render's proxy range differs. Adjust `ForwardedHeaders__KnownNetworks__0/1` to the range shown in the API log. |
