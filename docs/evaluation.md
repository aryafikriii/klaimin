# Receipt extraction evaluation

The evaluation command uses the 100-receipt CORD v1 test split. CORD is licensed under CC BY 4.0. Its labels support total, line-item names, and line-item prices; they do not label receipt dates or categories.

Download the test split to the locally excluded `.scratch` directory:

```powershell
dotnet run --project src/Klaimin.Evaluation -- download-cord .scratch/v1/cord-v1
```

The command reads the same user-secrets store as the web app for `Extraction:ApiKey`. `Extraction:Endpoint` can be set in user secrets or through the `Extraction__Endpoint` environment variable. Environment variables override user secrets. Run a model for a chosen number of receipts, with an optional delay in seconds between requests (default 10):

```powershell
dotnet run --project src/Klaimin.Evaluation -- gemini-2.5-flash 10 10
```

The run writes `results/<model>.json` after every receipt and resumes from that file when rerun. Do not commit downloaded images. The generated result file records the dataset source and attribution, scored receipt count, per-field accuracy, and each receipt's elapsed time and token counts.

## Reading the numbers

Each field is scored once per receipt, as right or wrong:

- **Total** is right when it equals the labelled total.
- **Line-item names** are right only when the whole list matches the label, item by item and in the same order. Letter case and surrounding spaces are ignored. One missing, extra, or misread item makes the receipt wrong for this field.
- **Line-item prices** follow the same all-or-nothing rule, with exact amounts.

CORD also labels sub-items, such as a topping under a dish, and these count as line items. A model that folds a sub-item into its parent line is scored wrong on names and prices for that receipt.

So 50% on line-item names means half of the receipts had a perfect item list, not that half of the items were misread. A receipt whose label lacks a field is left out of that field's count, which is why the total can be scored on fewer receipts than were run.

The committed run covers 10 receipts with one model. That is enough to show the method and too few to rank models.

Dataset: [CORD v1](https://huggingface.co/datasets/naver-clova-ix/cord-v1), linked by the [upstream CORD project](https://github.com/clovaai/cord). Attribution: Seunghyun Park et al., “CORD: A Consolidated Receipt Dataset for Post-OCR Parsing” (2019), CC BY 4.0.
