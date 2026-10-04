# Deploy to Render with a Neon database

The .NET API (a Docker web service) and the Angular site (a static site) run on Render; the Postgres database is a free Neon project. `render.yaml` at the repository root describes the two Render services.

Neon is used instead of Render's own free Postgres because Render deletes that database after 30 days. SQLite is not an option: the data layer relies on Postgres features (schemas per module, `xmin` concurrency tokens, `ILIKE`) and its migrations are Postgres-only.

## Free-plan limits to know about

- A free Render web service sleeps after about 15 minutes without traffic; the first request afterwards takes up to a minute.
- A free Neon database stops its compute after 5 minutes idle and wakes on the next query (about a second). It holds 0.5 GB.
- The API can only e-mail one-time codes (`Identity__OtpDelivery__Provider=Smtp`). SMS is not supported yet, so use the e-mail channel, and give the API a mail server (a free Brevo or Gmail SMTP account works).

## 1. Create the Neon database

1. At neon.tech create a project (Postgres 16 or newer) and a database, for example `examplatform`.
2. On the project's **Connect** panel turn **Connection pooling off** and copy the connection details: host, database, role and password. A direct connection is needed for restoring data and migrations.

Your Npgsql connection string is:

```
Host=<host>;Port=5432;Database=<database>;Username=<role>;Password=<password>;SSL Mode=Require;Trust Server Certificate=true
```

The equivalent URL for `pg_restore` is `postgresql://<role>:<password>@<host>/<database>?sslmode=require`.

## 2. Load your existing data

Run these in PowerShell, from any folder. First find the container name:

```powershell
docker ps --format "{{.Names}}  {{.Image}}"
```

Use the name of the `postgres:16` row as `<name>`. Dump inside the container (PowerShell's `>` would corrupt a binary dump), then restore from the same container, which has `pg_restore` 16:

```powershell
docker exec <name> pg_dump -U examplatform -d examplatform --no-owner --no-privileges -Fc -f /tmp/examplatform.dump
docker exec <name> pg_restore --no-owner --no-privileges --dbname "<the Neon URL>" /tmp/examplatform.dump
```

Keep a local copy as a backup with `docker cp <name>:/tmp/examplatform.dump $HOME\examplatform.dump`. The file holds real candidate data and password hashes, so keep it out of git and delete it when you no longer need it.

Check the copy by comparing row counts in both databases:

```powershell
docker exec <name> psql -U examplatform -d examplatform -c "select count(*) from identity.\"Users\";"
docker exec <name> psql "<the Neon URL>" -c "select count(*) from identity.\"Users\";"
```

Your local database is not changed by any of this. Do not delete the Docker volume `pgdata` (`docker compose down -v`) until you are sure the copy is complete.

The dump includes every module's migration history, so the API applies only the migrations your database does not have yet when it starts. Restore before the API's first start; if it already started against the empty database, add `--clean --if-exists` to `pg_restore`.

## 3. Create the Render services

1. In Render choose **New > Blueprint**, pick this repository and apply `render.yaml`.
2. On the `exam-platform-api` service, **Environment**, fill in the values marked `sync: false`:

| Key | Value |
|---|---|
| `ConnectionStrings__Postgres` | The Npgsql string from step 1. |
| `Cors__AllowedOrigins__0` | The site's address, e.g. `https://exam-platform-web.onrender.com` (no trailing slash). |
| `Smtp__Host`, `Smtp__User`, `Smtp__Password`, `Smtp__From` | Your mail server. `Smtp__Port` (587) and `Smtp__EnableSsl` are already set. |
| `Invite__LinkBaseUrl` | The site's address, so invitation links open the site. |

`Jwt__SigningKey` is generated for you. Everyone has to sign in again after it changes.

If Render gives the API a different address than `https://exam-platform-api.onrender.com`, change `API_BASE_URL` on the `exam-platform-web` service and redeploy it: the site is built with that address.

## 4. Check it

- `https://<api>/v1/health` returns `Healthy`.
- The API log shows `Migrated and seeded <Module>` for each module.
- Sign in with a candidate e-mail address; the code arrives by e-mail.

Staff sign in with a password and then get a code by e-mail, so the administrator's address must be one you can read.
