# V01-023 Web与NAS对齐

## 状态

实现已提交，ready_for_review，等待V01-021集中验收。分支codex/v01-023-web-first-release-alignment；实现bc3d151a681485e93960967c0c9be9c44bf7dc04。已合入冻结ced5751，读取任务包、ADR0014/0015、docs12并实际查看assets/visuals/10_web_admin_asset_browser.png。

## 差距与变更

现有三栏、顶部搜索、选中详情及窄屏结构满足本次冻结范围，没有必要重做UI。仅确认两处使用差距：登记说明未区分NAS宿主/容器/浏览器电脑路径；扫描完成没有解释固定快照与刷新含义。

- 登记帮助以部署挂载表为准，示例/assets/photos或子目录，明确NAS宿主路径和浏览器盘符不能直接作为容器路径；Windows原生仍提供服务端盘符与UNC例子。
- 已与V01-022部署owner确认固定映射/assets/<source_key>，photos对应/assets/photos。前端不新增container_path、不推断或改写路径。
- 首次扫描完成提示说明当前是索引快照，刷新不会重新扫描目录。
- Web README同步NAS/Windows语义和V01-021验收归属。
- 既有登记测试改用photos源和容器路径，并检查关联帮助/快照提示；既有失败重试用例继续使用Windows盘符。未增加测试数量或快照。

## 复用、影响与边界

沿用RegisterLibraryForm现有aria-describedby、弹窗滚动和焦点处理；LibraryScanStatus原状态条件；生成SDK、同源Cookie/CSRF、登记幂等及核心权限/路径校验未变。没有新依赖、语言、框架、CSS、布局、Host、SQL或wire变更，也没有启用设计稿中的预览/写文件/标签/查重/统计。

变化仅为常量文案渲染，与50万资产数量无关，不改变列表复杂度、分页、网络请求或原文件安全。手动阅读完整diff确认5个实现文件均属于任务；生产源只有2个组件的说明文本。

## 验收状态

按用户“集中实现、最后统一验收”的明确要求，本任务没有运行测试、formatter、lint、typecheck、构建或截图检查；不声明通过。仅完成源码/设计图阅读、与部署owner对齐、人工diff审查和Git提交。待根统一安排既有40项浏览器、格式/类型/构建及真实NAS容器/浏览器验收。

建议顺序：冻结ADR → V01-022部署与本提交 → V01-021统一验收。若挂载映射后续变化，登记例子和说明需随协调器一起调整；当前未发现需扩展功能的理由。
