# Offline Companions Toolkit

[中文](#中文) | [English](#english)

---

## 中文

**Offline Companions Reborn 的辅助工具集**：只捡战利品、伙伴状态监控、地面清扫。不修改原模组的任何文件，全部通过 Harmony 补丁 + 组件名识别实现。

### 功能

| 功能 | 说明 |
|---|---|
| **拾取过滤** | 白名单/黑名单两种模式，单一列表 + `-` 例外语法。在**扫描阶段**就让伙伴认为"放不下"，所以它不会走过去、也不会把不需要的东西吸到脚边；`Humanoid.Pickup` 再兜一层 |
| **伙伴监控窗口** | 屏幕角落列出你自己的伙伴：绿/黄/红三档状态（区域在跑 / 加载但停用 / 已卸载）、当前工作、距离、`移动中 / 静止 N 分` |
| **地面清扫** | 原模组的自动拾取只处理 `m_autoPickup == true` 的物品，而任何角色丢到地上的东西都会被置为 false。清扫补这一块：只要白名单允许，脚边的东西都会捡——包括你亲手扔给狼的肉 |

### 依赖

- **BepInExPack Valheim** 5.4.2360+
- **Offline Companions Reborn** 1.5.8+（必需，本工具没有独立功能）

### 安装

用 r2modman / Thunderstore Mod Manager 安装，或把 `plugins/ChineseCanFly.OfflineCompanionsToolkit.dll` 放进 `BepInEx/plugins/`。

### 配置

首次启动生成 `BepInEx/config/ChineseCanFly.OfflineCompanionsToolkit.cfg`，也可在游戏内 `F8` 面板里改。

`[2 - Filter]` 是核心：

```ini
Mode  = AllowList          # AllowList = 只捡列表里的；DenyList = 列表里的是禁捡
Items = Trophy*, *Spear*   # 单一列表，逗号分隔，支持 * 和 ?，- 开头是例外
```

| 想做的事 | 配置 |
|---|---|
| 打猎只捡战利品头 | `Items = Trophy*, *Spear*` |
| 只是别捡垃圾 | `Mode = DenyList`，`Items = RawMeat, DeerHide, Wood, Stone` |
| 战利品但不要灰矮人头 | `Items = Trophy*, *Spear*, -TrophyGreydwarf*` |
| 顺手捡肉 | `Items` 里加 `*Meat*`（前后都要有 `*`） |

`[3 - Monitor]`：`Toggle Hotkey`（默认 F7）、`Watch List`（空 = 全部）、位置/字号、`Only My Companions`。
`[4 - Sweep]`：`Radius`（默认 10 米）、`Interval Seconds`、`Max Per Pass`。

### 已知限制

- 过滤**不分工种**：`Wood / Stone / Ore / Forage`、`Smelt`、`Farm` 这些靠捡原料干活的工作会失效，需要用它们时把 `[1 - General] Enabled` 设为 `false`。
- 只收录原版战利品的预制体名（`Trophy*`），其它模组新加的战利品默认不匹配，可按需补。

### 兼容与免责

- 与 **Maddhattr** 无关，非官方扩展；只使用其公开组件与 ZDO 字段，不包含原模组的任何代码，也不重新分发其文件。
- 若原模组大版本更新改了内部字段名，监控窗口可能失效（会打印警告而不会崩溃）。

---

## English

A small companion toolkit for **Offline Companions Reborn**: loot filtering, a live roster overlay, and a ground sweep.

- **Loot filter** – one list with `*`/`?` globs and `-Name` exceptions, in `AllowList` or `DenyList` mode. Filtering happens at the scan stage, so companions never walk to or vacuum up rejected items; `Humanoid.Pickup` is a second safety net.
- **Roster overlay** – green / yellow / red state, current job, distance, and idle time ("moving" / "idle 12m").
- **Ground sweep** – picks up items the mod ignores because `m_autoPickup` was cleared when a character dropped them (e.g. meat you threw to your wolves).

**Requires** BepInExPack 5.4.2360+ and Offline Companions Reborn 1.5.8+.

Config: `BepInEx/config/ChineseCanFly.OfflineCompanionsToolkit.cfg` (or the in-game F8 menu). F7 toggles the overlay.

**Limitation:** the filter has no job awareness — wood/stone/ore gathering, smelting and farming stop working while it is enabled. Set `Enabled = false` to restore them.

**Disclaimer:** unofficial extension, not affiliated with Maddhattr. No code from the original mod is included or redistributed.

MIT licensed. Source: <https://github.com/tutu8420/Offline-Companions-Toolkit>
