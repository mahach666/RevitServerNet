# CHANGES

## 1.1.2

- Added `RevitServerUiApi` for `RevitServerAdmin{YEAR}/api/...` endpoints (Admin UI API).
- Added UI DTO models (`UiModelDetails`, `UiItemLockData`, `UiModelHistoryItem`, `UiTreeItem`) in `UiModels.cs`.
- `RevitServerApi`: generate `RevitServerAdminRESTService` path automatically:
  - <= 2012: `RevitServerAdminRESTService`
  - > 2012: `RevitServerAdminRESTService{YEAR}`
- `RevitServerUiApi`: generate `RevitServerAdmin` virtual directory automatically:
  - <= 2012: `RevitServerAdmin`
  - > 2012: `RevitServerAdmin{YEAR}`


