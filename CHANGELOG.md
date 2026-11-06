# Changelog

## 1.0.0

首次公开发布（从本地开发版 0.6.2 更名而来）。

- 更名：插件显示名 `Companion Toolkit` → `Offline Companions Toolkit`，GUID `ChineseCanFly.CompanionToolkit` → `ChineseCanFly.OfflineCompanionsToolkit`
- 拾取过滤：单一列表 + `AllowList` / `DenyList` 两种语义，支持 `*` `?` 与 `-Name` 例外；在扫描阶段（`Inventory.CanAddItem`）与兜底阶段（`Humanoid.Pickup`）两层拦截
- 伙伴监控窗口：绿/黄/红三档、当前工作、距离、`移动中 / 静止 N 分`、监控名单实时刷新
- 地面清扫：捡起 `m_autoPickup == false` 的地面物品（他人丢弃、狼咬死的猎物等），按白名单过滤
- 全部状态来自 ZDO 字段（`HC_Name` / `HC_Owner` / `HC_ActionMode`），伙伴卸载后仍可显示
