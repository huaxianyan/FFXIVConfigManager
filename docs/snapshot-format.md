# 快照格式 v1

FFXIVConfigManager 快照是扩展名为 `.ffxivconfig.zip` 的标准 ZIP 文件，不依赖专有容器。

## 目录结构

```text
manifest.json
files/
  ADDON.DAT
  COMMON.DAT
  ...
```

快照中的路径统一使用 `/`，不允许绝对路径、`..`、反斜杠、盘符、重复路径或 Manifest 未声明的条目。

## Manifest

`manifest.json` 使用 UTF-8 JSON，关键字段包括：

```json
{
  "formatVersion": 1,
  "snapshotId": "00000000-0000-0000-0000-000000000000",
  "createdAtUtc": "2026-08-11T03:00:00+00:00",
  "reason": "Manual",
  "source": {
    "profileId": "00000000-0000-0000-0000-000000000000",
    "profileName": "国际服",
    "characterFolder": "FFXIV_CHR0000000000000000"
  },
  "files": [
    {
      "archivePath": "files/ADDON.DAT",
      "originalFileName": "ADDON.DAT",
      "size": 106528,
      "lastWriteTimeUtc": "2026-08-11T03:00:00+00:00",
      "sha256": "...64 hexadecimal characters..."
    }
  ]
}
```

## 完整性规则

- 每个文件必须与 Manifest 中记录的长度和 SHA-256 一致；
- Manifest 格式版本必须等于读取方支持的版本；
- 当前实现最多接受 500 个 ZIP 条目；
- 单个配置文件解压后不得超过 64 MiB；
- 所有配置文件解压后的总大小不得超过 512 MiB；
- Manifest 不得超过 1 MiB；
- 快照创建期间先写入暂存目录和临时 ZIP，完整校验成功后才发布最终文件。

Manifest 和限制会随格式版本演进；旧版本快照不会在原地修改。
