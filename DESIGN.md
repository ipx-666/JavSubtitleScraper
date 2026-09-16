# JavSubtitleScraper 设计说明

## 1. 目标

JavSubtitleScraper 是一个 Emby 服务端插件。它从媒体文件名或目录名识别 JAV 番号，按优先级搜索中文字幕，下载后保存到视频所在目录，由 Emby 负责后续识别。

## 2. 扫描入口

### 2.1 插件手动扫描任务

`SubtitleScanTask` 实现 `IScheduledTask` 和 `IConfigurableScheduledTask`。用户可以在 Emby 计划任务页面手动运行“扫描并下载字幕”任务。

任务通过 `ILibraryManager` 获取 `Movie` 和 `Video` 媒体项，读取路径和 `RunTimeTicks`，将时长转换为毫秒，然后并发处理文件。并发数由 `MaxConcurrency` 控制，范围为 1～8。

### 2.2 定时扫描任务

`ScheduledSubtitleScanTask` 根据定时配置生成 Emby 计划任务触发器，执行时复用 `SubtitleScanTask` 的扫描流程。

### 2.3 媒体库事件

`LibraryEventEntryPoint` 监听媒体新增和更新事件，读取媒体路径及 `RunTimeTicks`，异步处理单个媒体项。媒体库事件入口不会执行全库强制重扫。

## 3. 配置

配置由 `PluginConfiguration` 持久化，配置页通过 Emby 插件配置接口读写。

- `EnableManualScan`：允许手动扫描。
- `EnableScheduledScan`：启用定时扫描。
- `EnableLibraryEvents`：监听媒体库新增和更新。
- `ForceFullScan`：插件手动扫描任务中忽略已有字幕检查。
- `OverwriteExistingSubtitles`：覆盖目标字幕文件；仅在计划任务中启动 JavSubtitleScraper 扫描任务时生效。
- `TargetLanguage`：目标语言，当前默认 `zh-CN`。
- `MaxConcurrency`：扫描并发数。
- `EnableDurationFilter`：仅对迅雷 API 返回结果启用时长过滤。
- `DurationFilterLowerPercent` 和 `DurationFilterUpperPercent`：时长比例范围，运行时分别限制为不高于 85% 和不低于 115%。
- `ScheduleMode`、`DailyTime`、`WeeklyDay`、`WeeklyTime`：定时任务设置。

完整时长作品才适合启用时长过滤。剪辑作品和没有可靠媒体时长的 STRM 文件应关闭此选项。

## 4. 字幕搜索链路

字幕源通过 `ISubtitleSource` 统一适配，`SubtitleSourceChain` 按顺序尝试：

1. 迅雷 API（`XunleiSubtitleSource`）。
2. 迅雷无有效结果时回退到 SubtitleCat（`SubtitleCatSource`）。

单个字幕源失败不会阻断后续字幕源。

### 4.1 迅雷 API

迅雷结果先进行番号强校验。列表中至少存在一个与请求番号完整匹配的候选时才继续筛选；否则该源失败并回退 SubtitleCat。明确属于其他番号的条目会被排除，哈希命名等无法确认番号的条目在整体校验通过后仍可作为候选。

候选按语言优先级排序：简体中文、未区分简繁的中文、无语言后缀或哈希命名、繁体中文；明确的其他语言排除。`languages` 字段中的简体标记优先于文件名判断。相同语言等级内按翻译质量排序，并保持 API 原始顺序。

启用时长过滤且媒体时长、字幕时长均有效时，字幕时长必须位于配置的下限和上限之间；否则跳过该候选。任一时长缺失时不执行过滤。

### 4.2 SubtitleCat

搜索请求使用 `show=1000` 展开结果，搜索页结果去重后最多取前 5 个详情页并发请求。

详情页结果按以下三层排序：

1. `translated from Chinese`；其中包含“精翻”的结果优先，然后按 languages 数、downloads 数、页面原始顺序。
2. `translated from Japanese`；按 languages 数、downloads 数、页面原始顺序。
3. 其他结果；按 languages 数、downloads 数、页面原始顺序。

详情页只接受明确的 `-zh-CN.srt` 和 `-zh-TW.srt` 下载链接，简体优先，繁体作为回退。最高优先级详情页找到简体字幕时可提前返回。

## 5. 下载与保存

字幕候选统一使用 `SubtitleCandidate` 表示，包含来源、下载地址、语言、格式、标题和排序信息。

字幕保存到视频所在目录，基本命名格式为：

```text
所有来源的字幕统一保存为：

```text
视频名.zh-CN.srt
```
```

下载先写入随机临时文件，成功后移动到目标路径；失败时清理临时文件。目标文件已存在且未启用覆盖时，不替换原文件。

## 6. 已有字幕与异常处理

普通扫描发现目标语言字幕后跳过。插件手动扫描任务在 `ForceFullScan` 开启时忽略这项检查；媒体库事件入口仍按已有字幕检查处理。

每个媒体文件独立处理。网络请求、解析或写入失败会记录日志并继续处理其他文件；收到取消信号时停止新增工作并向上抛出取消异常。

## 7. 日志

日志使用可搜索的结构化前缀记录扫描、匹配、来源、候选、下载和异常。媒体库事件会记录 `RunTimeTicks` 及转换后的毫秒时长，便于确认时长过滤输入是否有效。

日志中不得输出 Cookie、Token 或完整认证请求头。
