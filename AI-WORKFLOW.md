# AI-assisted development workflow

ClaimsModule was developed with the help of Codex. The work began with an analysis of the assessment documents and continued through backend and frontend implementation, testing, deployment and review. The [AI history](AI-HISTORY.md) contains selected excerpts from the five chats assigned to the ClaimsModule project.

## Roles

- **The candidate** defined priorities, clarified requirements, made decisions about the solution, requested reviews and authorized subsequent fixes. The candidate also participated in Azure and Swagger checks.
- **The primary Codex agent** analyzed the task and existing code, proposed the implementation and verification plan, made changes, ran checks and reported the results.
- **The Mentor** was a separate subagent used in later implementation work. It reviewed the task, relevant code and proposed plan before implementation, looking for gaps, risks, missed scenarios and better approaches. When it raised a concern, the primary agent revised the plan and submitted it again for review.
- **The Reviewer** was another subagent. After implementation and testing, it reviewed the new and changed code together with the test results, looking for bugs and regressions. Findings returned the work to analysis and planning, followed by another implementation, verification and review cycle.

## Development cycle

1. **Analyze the task.** Read the applicable requirements, inspect the existing implementation and tests, and identify the behavior and constraints affected by the change.
2. **Plan and consult the Mentor.** The primary agent describes the intended changes and verification. The Mentor reviews that plan. Implementation starts after the plan has been revised for its findings and approved.
3. **Implement and test.** The primary agent implements the approved plan, adds or updates tests for changed behavior and runs the relevant checks. Backend changes are checked with the solution test suite. SQL-dependent behavior is checked against SQL Server when available.
4. **Request an independent review.** The Reviewer receives the task, the cumulative changes and the test results. It checks the changes for errors, missing cases and regressions.
5. **Repeat when needed.** Each review finding becomes a new problem to analyze and plan. After the fix and tests, the Reviewer checks the cumulative changes again. The agent reports completion when the review has no outstanding findings, including any limits on verification.

This cycle was introduced after the initial backend work. The requirements analysis, early implementation and some deployment work preceded it. [Conversation](AI-HISTORY.md#backend-implementation-and-review)

## Project use

The candidate first asked Codex to analyze both assessment documents, document the requirements and propose the architecture before writing code. The work then started with authorization, followed by FNOL and the other backend capabilities. Angular implementation used the API contract and was checked against the requirements.

The candidate later requested a global review of the code and deployed application before authorizing fixes. Codex used automated tests, browser scenarios, Swagger and Azure logs to investigate findings and verify changes. The deployed retest used synthetic claims and covered the flow from FNOL through reserve approval, GL posting, document download and audit. [Review history](AI-HISTORY.md#global-review-and-azure-retest)

## Representative prompts

| Prompt | Purpose |
| --- | --- |
| “поки не пиши код, перший етап це просто загальний аналіз вимог” | Establish scope and requirements before implementation. [History](AI-HISTORY.md#requirements-and-architecture) |
| “можливо краще почати з авторизації?” | Start with identity so the first claim writes could use a tenant and audit actor. [History](AI-HISTORY.md#requirements-and-architecture) |
| “додай у воркфлоу щоб міграції теж застосовувалис” | Add database migrations to deployment after a missing-table error. [History](AI-HISTORY.md#azure-migrations) |
| “Перевір всі вимоги і проаналізуй фронт чи все реалізовано?” | Compare the Angular implementation with the requirements. [History](AI-HISTORY.md#angular-implementation-and-validation) |
| “зміни успішно розггорнуті, проведи другий раунд рев'ю” | Retest the deployed fixes and repeat the Azure review. [History](AI-HISTORY.md#global-review-and-azure-retest) |

## Examples of corrections

1. **Concurrent reserve resubmissions.** The Reviewer found that two parallel replacement submissions could create two active transactions for one rejected reserve. The implementation was revised to check for an active replacement under the claim write lock, with integration coverage for the decision workflow. [History](AI-HISTORY.md#backend-implementation-and-review) · [Tests](tests/ClaimsModule.IntegrationTests/Claims/ReserveDecisionApiTests.cs)
2. **GL recovery fairness.** Review identified that a busy organization could delay recovery for other tenants. The recovery job was revised to bound work per cycle and track progress per organization. [History](AI-HISTORY.md#backend-implementation-and-review) · [Recovery code](src/ClaimsModule.Infrastructure/Jobs/ReservePostingRecovery.cs)
3. **FNOL retry after a lost response.** Review found that the UI could wait indefinitely and that retry behavior needed to preserve the idempotency key. The request was given a timeout and retained its key for retry. The Azure retest withheld a successful response and verified that resubmission returned the same claim. [History](AI-HISTORY.md#global-review-and-azure-retest)

## Verification and responsibility

The candidate reviewed the agent's output and directed the work. Codex supplied implementation and test results, while the Mentor and Reviewer provided additional checks. Automated tests, SQL Server integration tests, browser tests and Azure checks provided evidence for individual changes. Their results were reported for the runs actually performed, including skipped or unavailable checks.

The recorded Azure retest included 142 API checks and a browser walkthrough. Backend CI ran 142 tests with SQL Server and frontend CI ran 15 Angular tests for the referenced revision. [Backend CI](https://github.com/Picadio/ClaimsModule/actions/runs/45168641489) · [Frontend CI](https://github.com/Picadio/ClaimsModule-frontend/actions/runs/45168684301)

## Where AI helped most

AI assistance was most valuable when turning business rules into edge cases and tracing failures across layers. Reviewing reserve submissions and GL recovery surfaced concurrency and tenant fairness issues that the normal demo flow did not exercise. Tracing an FNOL request from the Angular form through the API identified how a lost response could lead to a duplicate attempt and what the retry test needed to prove. [Backend review](AI-HISTORY.md#backend-implementation-and-review) · [Azure retest](AI-HISTORY.md#global-review-and-azure-retest)

The candidate's input mattered most for requirements interpretation and deployment. The candidate chose the development order, questioned how the assessment described the five backend projects and helped check Azure Blob configuration and SAS downloads. SQL Server and Azure tests supplied evidence for behavior that code inspection alone could not verify. [Requirements discussion](AI-HISTORY.md#requirements-and-architecture) · [Blob verification](AI-HISTORY.md#swagger-and-blob-testing)
