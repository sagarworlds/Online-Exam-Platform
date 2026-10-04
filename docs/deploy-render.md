# Deploy to Render

Everything runs on Render: the Postgres database, the .NET API (a Docker web service) and the Angular site (a static site). `render.yaml` at the repository root describes all three.

## Free-plan limits to know about

- A free web service sleeps after about 15 minutes without traffic; the first request afterwards takes up to a minute.
- A free Render Postgres database is deleted 30 days after it is created. Upgrade it before then, or point `ConnectionStrings__Postgres` at another Postgres host.
- The API can only e-mail one-time codes (`Identity__OtpDelivery__Provider=Smtp`). SMS is not supported yet, so use the e-mail channel, and give the API a mail server (a free Brevo or Gmail SMTP account works).

## 1. Create the services

1. In Render choose **New > Blueprint**, pick this repository and apply `render.yaml`.
2. When asked for the values marked `sync: false`, leave them empty for now; step 3 fills them in.

## 2. Load your existing data

Run on your machine while the Docker database is up (names from `docker-compose.yml`):

```bash
docker compose exec -T postgres pg_dump -U examplatform -d examplatform \
  --no-owner --no-privileges -Fc > examplatform.dump

pg_restore --no-owner --no-privileges --dbname "<External Database URL from Render>" examplatform.dump
```

The dump holds real candidate data and password hashes. Keep it out of git and delete it afterwards.

The dump includes every module's migration history, so the API applies only the migrations your database does not have yet when it starts.

## 3. Fill in the API settings

On the `exam-platform-api` service, **Environment**:

| Key | Value |
|---|---|
| `ConnectionStrings__Postgres` | `Host=<host>;Port=5432;Database=<db>;Username=<user>;Password=<password>;SSL Mode=Require;Trust Server Certificate=true`, from the database's **Connections** page. Npgsql needs this form, not the `postgres://` URL. |
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
