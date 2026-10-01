# Policy rules are code, not model output

A model reads the receipt image and proposes its fields and a category. It does not decide whether a receipt breaks policy, and its extraction is never saved until the claimant has confirmed it. Caps, the finance threshold, and duplicate checks run as ordinary tested code against the confirmed values.

We chose this because a reimbursement decision has to be the same every time for the same input and has to be explainable to the person who was flagged. A model asked to judge policy gives neither guarantee, and its mistakes would be paid out in money.

## Consequences

Changing a policy rule means changing code or admin-managed data, not a prompt. The evaluation page measures extraction only, because that is the only thing the model is trusted with.
