# 按谱解除代数 V1 实现设计

## 状态与边界

这是 P0 的已实现 Migration 技术契约，不是生产候选功能开关。`20261030000200_AddScopedUnlinkGenerationV1` 已按本文创建阴影结构；当前应用仍只以 `UnlinkGenerationV1` 的全库代数保护解除候选。不得因该 Migration 切换候选读取/解除 SQL、启用自动合并或启用生产恢复。

现有 `UnlinkGenerationV1` 的表和九个触发器必须保留。V1 按谱触发器与其并行执行，因此旧版应用只写既有业务表时，提交后的写入会同时递增全库与所属谱的代数。

## 不可变对象名称与 SQL 生成契约

实现必须新增内部静态 `ScopedUnlinkGenerationV1`；其中的表定义、触发器定义、名称清单和 guard SQL 都是版本化常量。已部署后不得修改其返回字符串；改变规则只能增加 V2 Migration 和 V2 常量。所有 `sqlite_master.sql` 对比使用该常量产生的完整字符串，不作大小写、空白或片段匹配。

|对象|V1 名称|固定定义/数量|
|---|---|---|
|活动代数表|`ScopedUnlinkGenerationV1`|`CREATE TABLE ScopedUnlinkGenerationV1 (GenealogyId TEXT PRIMARY KEY, Value INTEGER NOT NULL CHECK (typeof(Value) = 'integer' AND Value >= 0))`|
|墓碑表|`ScopedUnlinkGenerationTombstoneV1`|`CREATE TABLE ScopedUnlinkGenerationTombstoneV1 (GenealogyId TEXT PRIMARY KEY, Value INTEGER NOT NULL CHECK (typeof(Value) = 'integer' AND Value >= 0))`|
|族谱建立触发器|`ScopedUnlinkGenerationV1GenealogiesINSERT`|1|
|族谱删除触发器|`ScopedUnlinkGenerationV1GenealogiesDELETE`|1|
|图数据触发器|`ScopedUnlinkGenerationV1{Table}{Operation}`|`People`、`PersonRelationships`、`FamilyUnions` 各 `INSERT`/`UPDATE`/`DELETE`，共 9 个|

两张表都**不得**有指向 `Genealogies` 的外键或级联。活动表只保存仍存在的族谱；墓碑只保存被删除族谱的最后代数。任一 GUID 在两表中至多出现一次。`FamilyBranches`、`GenealogyImportRecords` 与 `Genealogies` 的普通 `UPDATE` 不在 V1 受保护写入集合内：这与现有全库 `UnlinkGenerationV1.Tables` 保持一致，且当前解除候选并不以它们作为条件。

### 固定触发器语义

实现以固定表数组和操作数组生成下面的精确单行 `CREATE TRIGGER` 字符串；数组不得由数据库输入或外部配置产生。

1. `Genealogies INSERT`：若活动表已有 `NEW.Id`，或同 ID 墓碑不是 `[0, 9223372036854775806]` 的整数，`RAISE(ABORT, 'invalid scoped unlink generation')`；随后插入活动行。无墓碑时值为 `0`，有墓碑时值为墓碑值加 `1`，然后删除该墓碑。因此删除后以同一 GUID 重建族谱不会回到 `0`。
2. `Genealogies DELETE`：先验证活动行有效且未到 `9223372036854775807`、同 ID 无墓碑；随后将 `活动.Value + 1` 写入墓碑，并删除活动行。SQLite 外键级联导致的 `People`、`PersonRelationships`、`FamilyUnions` 删除先各自递增活动行，族谱删除本身再递增一次并封存最终值。
3. 三个图数据表的 `INSERT`：验证 `NEW.GenealogyId` 的活动行是 `[0, 9223372036854775806]` 的整数，再将该行加一。
4. `DELETE`：同样验证并递增 `OLD.GenealogyId`。
5. `UPDATE`：先验证并递增 `OLD.GenealogyId`；若 `NEW.GenealogyId <> OLD.GenealogyId`，再独立验证并递增 `NEW.GenealogyId`。同谱更新只增加一次，合法跨谱移动增加旧谱和新谱各一次。

每个验证均采用 `SELECT CASE WHEN NOT EXISTS (...) THEN RAISE(ABORT, 'invalid scoped unlink generation') END`。比较上限必须发生在加一之前；缺失、负数、非整数、溢出、两表同 ID 或触发器缺失都不得静默补建、归零或继续写入。

`GuardSql(genealogyId)` 只从活动表读取该谱代数，并同时精确验证两张表和上述 11 个触发器的 `sqlite_master` 定义；它不查询墓碑作为可用候选。未来候选/解除路径接入前，guard 失败一律失败关闭。当前生产候选仍调用 `UnlinkGenerationV1.GuardSql`，不得在本 Migration 中替换。

## EF Migration 的顺序

Migration 类暂命名 `AddScopedUnlinkGenerationV1`；实际 Migration ID 在生成时确定。所有 `migrationBuilder.Sql` 均使用默认 `suppressTransaction: false`，不得在 SQL 内嵌 `BEGIN`、`COMMIT` 或 `PRAGMA foreign_keys`。SQLite provider 的迁移事务必须使 DDL、基线、触发器和 EF migration history 同成同败。

`Up` 的固定顺序如下：

1. 启动现有 `DatabaseMigrationService`；若有待执行迁移，先创建并验证迁移前快照。
2. 创建活动表，再创建墓碑表。
3. 在尚未安装任何按谱触发器时执行 `INSERT INTO ScopedUnlinkGenerationV1 (GenealogyId, Value) SELECT Id, 0 FROM Genealogies`，为已存在族谱建立基线；墓碑表保持空。
4. 创建两个 `Genealogies` 触发器，再按表、操作的固定顺序创建九个图数据触发器。
5. 在同一 Migration 事务末尾执行 `InstallValidationSql`：它精确比对两张表和 11 个触发器，并确认每个现存 `Genealogies.Id` 恰有一个有效活动行、墓碑不与活动行重叠。SQLite 的 `RAISE()` 只能用于触发器，故此 SQL 在检查失败时才向活动表插入固定验证 ID 与 `Value = -1`，由该表的既有 `CHECK` 约束使 Migration 中止；检查成功时零行插入。任一检查失败即回滚前四步。
6. `MigrateAsync` 返回后，以新只读上下文执行同一 guard/结构检查；失败则停止启动，不启用任何按谱候选操作，也不尝试运行时修复。

`Down` 仅用于开发、隔离验收和明确的 Migration 降级，不是生产恢复能力：先按反向固定顺序删除九个图数据触发器，再删除两个 `Genealogies` 触发器，随后删除活动表和墓碑表。使用不带 `IF EXISTS` 的 `DROP`；发现被篡改或缺失时失败并让该次降级回滚。它不删除或重建 `UnlinkGeneration` 及其触发器。重新升级会为仍存在的族谱重新建立 `0` 基线；因此在按谱候选路径另行获批前，禁止把降级/再升级当作保留按谱候选有效性的手段。

该 Migration 使用原始 SQL 阴影结构，不新增 DbSet 或领域实体；生成的 migration designer / model snapshot 仍须提交，并以 `has-pending-model-changes` 验证模型没有意外漂移。

## 锁、旧版共存与启动报告

数据目录租约仍是同版本实例的第一道保护，但不假定旧版或第三方遵守它。`DatabaseMigrationService` 在查询待迁移集合前即在 Migration 连接设置 `PRAGMA busy_timeout = 15000`；`DatabaseSnapshotService` 新开的源/目标连接也接收同一 15 秒设置，因此待迁移检查、快照和 `MigrateAsync` 都适用一致边界。不做隐藏重试、自动合并或绕过锁的第二连接。

- 先有外部写事务：Migration 最多等待 15 秒。它提交后，步骤 3 才读取现状并建立 `0` 基线；该先前写入不能被计为 Migration 后的按谱变更。超时或死锁报 `SQLITE_BUSY`/`SQLITE_LOCKED` 并停止启动。
- 先有 Migration 事务：旧模型普通业务写入按 SQLite 锁语义等待；Migration 提交后，旧模型仍可向既有表写入，双轨触发器会更新两种代数。旧模型若尝试结构变更、删触发器或替换数据库，不在共存支持范围内。
- 安装中断、取消或任何 SQL 失败：默认 Migration 事务回滚，旧表、现有全库代数和旧写入路径仍可用；保留迁移前快照，不自动继续、修复或恢复。

`DatabaseMigrationService` 的实现必须将主/扩展 SQLite 错误码为 `SQLITE_BUSY` (5) 或 `SQLITE_LOCKED` (6) 的异常包装为可区分的 `DatabaseMigrationBlockedException`，并保留内部异常。启动界面应显示“数据库正被另一写入占用，升级未完成；关闭占用程序后重试”，同时显示已创建的迁移前快照路径（如有）；日志记录错误码、数据库路径和快照路径，绝不报告升级成功。其他 Migration 异常沿用失败关闭报告，并同样保留快照路径。用户显式重启/重试才可再次进入 Migration。

## 受保护表重建

SQLite 重建 `People`、`PersonRelationships` 或 `FamilyUnions` 会删除附属触发器。任何会重建其中一表的后续 Migration 都必须在该表最终名称和最终列已就绪后、同一 Migration 事务内调用共享的重装生成器：先创建现有 `UnlinkGenerationV1` 三个触发器，再创建该表的三个 `ScopedUnlinkGenerationV1` 触发器，最后运行全量 `InstallValidationSql`。重建 `Genealogies` 时同样重装两个按谱族谱触发器；重建 `FamilyBranches` 或导入来源表不需重装按谱触发器。

运行期 guard 只检测、失败关闭，不自动重装。触发器重装不是修复被第三方篡改的线上库；它仅是经 Migration 审核的结构重建步骤。

## 实施前隔离验收

实施须以真实 `IMigrator`、正式 `BackupRestorePreflightService` 与 V1 常量替换测试专用安装器，至少覆盖：

1. 从 `20261030000100_AddUnlinkGeneration` 旧库升级，快照存在，活动行基线、11 个触发器和全库触发器完整；注入任一步失败时无 V1 表/触发器或 history 残留。
2. 外部写在升级前持锁时的提交基线、超过 15 秒的 `BUSY`/`LOCKED` 可读报告；安装先获锁时旧模型写入等待并在提交后同时更新两种代数。
3. 同谱 ABA、跨谱移动、删除/重建同 GUID、级联删除、无效/溢出活动或墓碑值和触发器替换均失败关闭；全库保护仍按既有规则工作。
4. SQLite 重建每个受保护表后，正式 Migration 重装两套触发器；未重装时 guard/解除拒绝。
5. 新程序拒绝旧备份、旧程序拒绝新备份，且正式预检不改活动库或包；不在预检或恢复期间迁移、转换或替换数据库。
6. 真实内部/外部导入的成功与注入失败保持图、来源、全库代数和按谱代数同一事务；WPF 候选/切谱/退出协调继续使用全库路径，直到另行评审切换。
