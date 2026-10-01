# klaimin

A web app for claiming back money spent for work. An employee photographs a receipt, the app reads its fields, the employee corrects and confirms them, and the claim goes to their manager and, above a set amount, to finance.

Receipts and amounts are Indonesian: whole Rupiah, written like Rp 125.000.

## What it does

A claimant starts a claim and uploads a photo of each receipt. A model proposes the total, the date, the line items, and a category. The claimant sees that proposal next to the photo, fixes what is wrong, and confirms. Only the confirmed values are saved. If the model fails or no API key is set, the same form opens empty and the claimant types the fields.

Two checks run on every confirmed receipt, and both are ordinary code, not model output:

- A receipt above its category's cap gets a policy flag. The claimant has to write a justification before the claim can be submitted.
- A receipt that looks like one already submitted gets a duplicate flag: either the photo is identical, or the total and date match another receipt from the same claimant. The flag links to the other receipt for people allowed to open it.

Flags never stop a claim. They put the question in front of the person who decides.

The claimant's manager approves, returns, or rejects the claim. A returned claim goes back to the claimant to revise and resubmit. An approved claim whose total is above the finance threshold then waits for finance, who decide it the same way. Every decision is kept with who made it, at which step, when, and their comment. Nobody can decide their own claim.

An admin manages the categories, their caps, and the finance threshold. A changed cap or threshold applies from then on and leaves earlier receipts and claims as they were.

## Why extraction is measured

A company deciding whether to trust a model with receipts needs to know how often it gets a total wrong. So the repo includes a command that runs the same extraction code over the public [CORD](https://github.com/clovaai/cord) receipt dataset and scores each field against the dataset's labels.

The committed run is small: `gemini-2.5-flash` on the first 10 receipts of the CORD v1 test split.

| Field | Receipts right | Receipts scored |
| --- | ---: | ---: |
| Total | 9 | 9 |
| Line-item names | 5 | 10 |
| Line-item prices | 8 | 10 |

Each receipt took about 10 seconds and about 1,170 tokens on average.

The line-item rows are stricter than they look. A receipt counts as right only when its whole list of items matches the label, in order, so one misread item fails the receipt. One of the 10 receipts has no labelled total, which is why that row is scored on 9. Ten receipts and one model show the method; they are too few to say how good the model is. [docs/evaluation.md](docs/evaluation.md) explains the scoring and how to run a larger evaluation, and the per-receipt results are in [results/gemini-2.5-flash.json](results/gemini-2.5-flash.json).

## Run it

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet run --project src/Klaimin.Web
```

Open http://localhost:5031. On first start the app creates its SQLite database and five accounts: a claimant, the claimant's manager, two finance users, and an admin. In the development environment the sign-in page has a button for each of them, so no password is needed.

Receipt reading is optional. Without an API key the app works through manual entry. To turn it on, store a key for a provider that speaks the OpenAI chat protocol. The defaults in `src/Klaimin.Web/appsettings.json` point at Gemini:

```
dotnet user-secrets set "Extraction:ApiKey" "<your key>" --project src/Klaimin.Web
```

A free-tier key may let the provider use what you send to improve its products. Use receipts that hold no personal or confidential data.

## Tests

```
dotnet test
```

Most tests start the real app in memory on a temporary database and drive it over HTTP the way each role would: sign in, upload, confirm, submit, decide. Receipt reading is the one part replaced by a fake, so the tests need no API key and no network. The evaluation scoring is tested directly as a pure function.

## How it is built

ASP.NET Core 10 MVC, EF Core with SQLite, ASP.NET Core Identity, and Microsoft.Extensions.AI for the model call. Pages are server-rendered with one small stylesheet and no front-end framework.

- `src/Klaimin.Core` holds claims, flags, approval, and the extraction contract.
- `src/Klaimin.Web` is the MVC app.
- `src/Klaimin.Evaluation` is the evaluation command.
- `tests/Klaimin.Tests` holds the tests.

The words used in the code and the interface are defined in [GLOSSARY.md](GLOSSARY.md). The decisions that would be hard to guess from the code are recorded in [docs/adr](docs/adr):

- [Policy rules are code, not model output](docs/adr/0001-policy-rules-are-code-not-model-output.md)
- [Flags inform people and never block](docs/adr/0002-flags-inform-people-and-never-block.md)
- [SQLite as the only database](docs/adr/0003-sqlite-as-the-only-database.md)

## Not included

Paying out approved claims, email notifications, currencies other than Rupiah, pages for managing users and reporting lines, and a hosted demo.

## Credits

The evaluation uses CORD: Seunghyun Park et al., "CORD: A Consolidated Receipt Dataset for Post-OCR Parsing" (2019), licensed under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). The receipt images are downloaded by a documented step and are not part of this repo.

## Licence

[MIT](LICENSE)
