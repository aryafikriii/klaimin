# klaimin

Employees claim back money they spent for work by submitting photographed receipts. The claim is checked against company policy and approved by people.

## Language

### Claims

**Claim**:
A request by one claimant to be reimbursed, made of one or more receipts and decided as a whole.
_Avoid_: Expense report, request, submission

**Receipt**:
One proof of purchase inside a claim: an image plus its confirmed total, date, category, and line items.
_Avoid_: Expense, struk, bill, invoice

**Line item**:
One purchased thing printed on a receipt, with a name and a price.
_Avoid_: Row, product, menu

**Claimant**:
The employee who submits a claim and receives the reimbursement.
_Avoid_: Requester, submitter, user

**Claim total**:
The sum of the confirmed totals of every receipt in a claim, in whole Rupiah.
_Avoid_: Amount, grand total

### Extraction

**Extraction**:
The receipt fields a model proposes from a receipt image. It is a proposal and is never stored as the receipt's data on its own.
_Avoid_: OCR result, scan, parse

**Confirmation**:
The claimant's review of an extraction, after which the corrected values become the receipt's data.
_Avoid_: Verification, validation

### Policy

**Category**:
The kind of spending a receipt belongs to, such as meals or transport. An admin manages the list.
_Avoid_: Type, expense type

**Cap**:
The highest receipt total a category allows before the receipt is flagged.
_Avoid_: Limit, ceiling, plafon, budget

**Flag**:
A marker on a receipt saying something needs a person's attention. A flag never stops a claim from being submitted.
_Avoid_: Violation, warning, error

**Policy flag**:
A flag raised because a receipt breaks a policy rule, such as exceeding its category's cap.

**Duplicate flag**:
A flag raised because a receipt looks like one already submitted, with a pointer to the other receipt.

**Justification**:
The claimant's written reason for submitting a receipt that carries a policy flag.
_Avoid_: Note, comment, explanation

### Approval

**Approver**:
A person who decides a claim at one approval step.
_Avoid_: Reviewer, checker

**Manager step**:
The first approval step, decided by the claimant's manager. Every claim passes through it.

**Finance step**:
The second approval step, decided by anyone in the finance role. Only claims whose claim total is above the finance threshold reach it.

**Finance threshold**:
The claim total above which a claim needs the finance step.
_Avoid_: Approval limit

**Approve**:
An approver's decision that moves a claim to the next step, or completes it at the last step.

**Return**:
An approver's decision that sends a claim back to the claimant to revise and resubmit.
_Avoid_: Send back, request changes, reject

**Reject**:
An approver's decision that ends a claim for good. A rejected claim cannot be resubmitted.
_Avoid_: Decline, deny

**Decision**:
One approve, return, or reject by an approver, with its comment and time. A claim keeps every decision made on it.
_Avoid_: Action, approval record

### Evaluation

**Evaluation run**:
One model extracting a fixed set of labelled receipts, scored field by field against the labels.
_Avoid_: Benchmark, test run
