# 产品发布说明

版本：`v2.4.0`

请在部署前确认 `{customer_id}` 已经绑定到
[管理控制台](https://example.com/admin)。

| 字段          | 说明         |
| ------------- | ------------ |
| `region`      | 目标部署区域 |
| `featureFlag` | 灰度开关     |

```json
{
    "featureFlag": "translation-preview",
    "enabled": true
}
```
