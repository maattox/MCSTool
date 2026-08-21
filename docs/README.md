# Documentation map

Read **only** what the current V1 / QA step names. Do not load this whole folder.

## Living (agents)

| Doc | Use |
|-----|-----|
| [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md) | **Execution checklist.** Implement only the step marked NEXT. |
| [`V1-QA-Pass-3-Scope.md`](V1-QA-Pass-3-Scope.md) | Current QA pass (blocked until the operator starts it). |
| [`V1-QA-Pass-3-Results.md`](V1-QA-Pass-3-Results.md) | Fill during Pass 3. |
| [`V1-QA-Catalog.md`](V1-QA-Catalog.md) | Test IDs / expected — named IDs only. |
| [`V1-Pass-2-Follow-On-Plan.md`](V1-Pass-2-Follow-On-Plan.md) | Step 8.4 notes. **P1–P13 DONE.** |
| [`Guide.md`](Guide.md) | Happy-path user guide. Update in the same step if UX changes. |
| [`PRODUCT-IDEAS.md`](PRODUCT-IDEAS.md) | Vision / MVP–v1–later. **Not infallible.** Named headings only. |
| [`Issues.md`](Issues.md) | Known quirks. File Setup/on-box/door bugs here. |
| [`Infrastructure-Information.md`](Infrastructure-Information.md) | OCI architecture (placeholders). |
| [`VM-Software.md`](VM-Software.md) | What is built on VM1 / door. |
| [`Door-VM-Control-Plane.md`](Door-VM-Control-Plane.md) | Door / `mccontrol` behavior. |
| [`Operator-Troubleshooting.md`](Operator-Troubleshooting.md) | SSH/OCI copy-paste. |
| [`Agent-Deploy-Pitfalls.md`](Agent-Deploy-Pitfalls.md) | SSH/sudo/SFTP mistakes (agents). |
| [`OCI-API-Usage.md`](OCI-API-Usage.md) | 429 / waiters / Object Storage thrift. |
| [`Local-Config.md`](Local-Config.md) | Gitignored `data/*.local.json` schema. |
| [`Contracts-Object-Storage.md`](Contracts-Object-Storage.md) | Bucket object names / JSON / writers. |
| [`Minecraft-Server-Deployment-Blueprint.md`](Minecraft-Server-Deployment-Blueprint.md) | Game install **mechanism** — named §§ only. |
| [`Automated-Infrastructure-Deployment.md`](Automated-Infrastructure-Deployment.md) | OpenTofu / Setup IaC **mechanism**. |
| [`Lab-Reference-Stack-Notes.md`](Lab-Reference-Stack-Notes.md) | Historical Resource Manager dump digest (copy vs skip). |
| [`Lab-IAM-Reference.md`](Lab-IAM-Reference.md) | Sanitized lab IAM statements (do not copy matching rules). |
| [`Sample-Packs.md`](Sample-Packs.md) | Operator-local pack fixtures. |
| [`V1-Bug-Fix-Plan-TEMPLATE.md`](V1-Bug-Fix-Plan-TEMPLATE.md) | Template after a QA pass is triaged. |

## Archive (do not execute)

Completed plans and historical QA live in [`archive/`](archive/README.md). Open those files only if a living step names them.
