# AgroVisionAI Mobile REST API (Task 6)

Task 6 adds a JWT Bearer API without changing the existing MVC cookie login. The website keeps working as before, while Android or another client can use JSON endpoints.

## Authentication flow

1. `POST /api/mobile/auth/register` or `POST /api/mobile/auth/login`.
2. Read `accessToken` from the JSON response.
3. Send it on protected requests as `Authorization: Bearer <token>`.
4. The default access token lifetime is 60 minutes and is configurable with `Jwt:AccessTokenMinutes`.

The current task intentionally uses access tokens only. A refresh-token rotation flow can be added later if long-lived mobile sessions are required.

## Main endpoints

- `POST /api/mobile/auth/register` - create account and return JWT.
- `POST /api/mobile/auth/login` - authenticate and return JWT.
- `GET /api/mobile/account/me` - current user profile.
- `GET /api/mobile/detections?page=1&pageSize=20` - current user's history.
- `GET /api/mobile/detections/{id}` - one result including model audit metadata and feedback.
- `POST /api/mobile/detections/analyze` - multipart upload. Field name: `cropImage`.
- `POST /api/mobile/detections/{id}/feedback` - save/update human feedback.
- `GET /api/mobile/detections/{id}/image` - protected source image.
- `GET /api/mobile/detections/{id}/explanation` - protected Grad-CAM image when available.

Every detection/history/image/feedback query is ownership-scoped to the authenticated user.

## Example login body

```json
{
  "email": "user@example.com",
  "password": "Password123"
}
```

## Example feedback body

```json
{
  "verdict": "Incorrect",
  "suggestedDisease": "Wheat Yellow Rust",
  "comment": "Field symptoms looked different."
}
```

Valid verdict values are `Correct`, `Incorrect`, and `NotSure`.

## Configuration

Public defaults are in `appsettings.Development.json`; committed signing keys are empty.
Copy `appsettings.Local.example.json` to the ignored `appsettings.Local.json` and set
`Jwt:Key` to a random secret of at least 32 characters. Follow the root README for setup.
Environment variables override local settings. Production must supply its own secret.

Docker Compose reads the production signing key from `.env`:

```text
JWT_KEY=<long random secret>
JWT_ISSUER=AgroVisionAI
JWT_AUDIENCE=AgroVisionAI.Mobile
JWT_ACCESS_TOKEN_MINUTES=60
```

Do not commit a real `.env` file or production JWT secret to Git.


## Weather disease risk (Task 7)

Authenticated mobile clients can request a Pakistan weather-based scouting advisory:

```http
GET /api/mobile/weather-risk?location=Sanghar&crop=Wheat
Authorization: Bearer <token>
```

Supported crops: `Cotton`, `Wheat`, `Rice`. The response contains the weather summary and per-disease Low/Moderate/High environmental favorability. It is an advisory, not a diagnosis.

## PDF diagnostic report (Task 10)

Authenticated mobile clients can download the same saved analysis as a PDF:

```http
GET /api/mobile/detections/{id}/report.pdf
Authorization: Bearer <token>
```

Optional query parameters:

- `daysToHarvest=20`
- `whiteflyTreatmentNeeded=true|false`

The report contains the saved AI result, model audit metadata, crop image, Grad-CAM image when available, human/expert feedback status, Pakistan crop-care guidance and evidence references. The endpoint always enforces detection ownership.
