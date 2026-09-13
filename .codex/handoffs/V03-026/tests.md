# V03-026 测试与交付记录

最终运行包：7f092904dbadcf1276bb9daf5b565946ecccbdad；Core/image/Worker源码与已执行原生c775b7d回归相同，后续仅修NAS CLI和Web样式。实际artifact、原始日志、TRX、截图及归档摘要见[same-nas-delivery/README.md](same-nas-delivery/README.md)。不得累加重叠套件或把缺夹具/平台跳过算通过。

| 验证 | 结果 |
| --- | --- |
| 锁定SDK10.0.111，solution locked restore、format、Release build | 通过，0编译警告 |
| solution普通测试（a87b44c） | 428通过、52平台/夹具跳过 |
| 最后worker/Preview build、变更源format、ImageInputPolicy与ImageContainer集合 | 38通过，其中4项为新用例；不再累计其余34项 |
| NuGet实际在线查询与依赖验证 | 14项目、50锁定包通过，无新第三方包 |
| Linux同一源Preview完整套件+源broker补充 | 131唯一用例，93过/38未执行；真实SO_PEERCRED与源broker通过 |
| Windows精确c775b7d NativeAOT旧LPAC+Job | 1/1，真实512×300 PNG、exit0；测试目录/worker清空 |
| Linux旧无参数seccomp图片路径 | JPEG/PNG/WebP×512/1600，6/6；全PNG CRC、exit0、容器清理 |
| NAS新监督器最终c775b7d | 10项metadata/32MiB边界与隔离probe通过，typedLimit计数0；GC128的40MP两profile此前已通过 |
| NAS故障注入 | Ready前/后/部分输入取消；上传时kill child、8秒超时、kill PID1与熔断重启；旧宿主PID1.414秒内消失 |
| NAS最终7f09290正式CLI | initialize/start/status、固定socket持久、实际容器配置/health通过 |
| NAS真实Core图片 | 20普通格式/拒绝 + 18最大值/元数据边界，共38项符合预期；PNG CRC/尺寸验证 |
| NAS图片服务中断/恢复 | 目录200、图片503；恢复后正常预期415，未清空fuse |
| Web源码/格式/生产build | 通过；SDK/Web frozen offline install，TS检查包含在build中 |
| Web浏览器 | 实际完整66/66通过，含两viewport的缩略图边界断言 |
| 最终NAS页面 | 1440桌面/390手机，无样式注入；缩略图/1600预览/Esc/退出清图通过，已看截图 |
| 生产升级 | 四旧卷+配置5个冷备compare/hash通过；原2库/195792观察及提交数、原账号/部署ID不变 |
| 正式库原图片 | 2张×2profile共4次200，未保存像素/名称/路径 |
| 原件与资源清理 | 20合成文件hash/mtime/大小不变；3容器/5卷/2网络/测试目录/原型IPC与4镜像清理，正式三服务仍健康 |

真实命令沿用项目入口：

```text
dotnet restore AssetLibrary.slnx --locked-mode
dotnet format AssetLibrary.slnx --verify-no-changes --no-restore
dotnet build AssetLibrary.slnx --configuration Release --no-restore
dotnet test AssetLibrary.slnx --configuration Release --no-build --no-restore
dotnet package list --project AssetLibrary.slnx --include-transitive --vulnerable --no-restore --format json --output-version 1
python -I -B scripts/validate_dotnet_dependencies.py --solution AssetLibrary.slnx --packages-dir <locked-cache> --vulnerability-report <report>
python -I -B scripts/validate_dotnet_source.py
python -I -B scripts/validate_web_source.py
pnpm --dir apps/web run format:check
pnpm --dir apps/web run build
pnpm --dir apps/web run test:browser -- image-preview.spec.mjs --grep "image preview preserves Quick Look"
python3 -I -B scripts/build_nas_deployment.py --docker <owned-sudo-wrapper> --output-root .runtime/sandbox-storage/V01-022/same-nas-final-7f09290
```

浏览器上述命令实际运行66项，非仅2项，已保留完整日志。Linux镜像从干净Git bundle克隆的精确提交构建；SDK/AOT锁和镜像中实际ELF/许可证/SourceRevision/SHA验证，无复用伪来源标签。images.tar为801215488字节，SHA256 3e2173c6849f3b76ffeef57275f16eaa1e5af89762d54523e30882b37662c2f8，Windows/NAS传输后复核通过。

历史失败保留：缺seccomp是原路径拒绝；40MP初始AS预留过高、31MiB巨大PNG块实际CPU2.97秒后exit137，分别修为GC128与非IDAT4MiB准入；Compose create --no-deps不支持已修并实际CLI通过；缩略图naturalWidth/toBeVisible绿但元素341px高被78px容器裁空，新增框内断言后在旧样式确定失败、修后实际78px完整显示。没有提高硬限或删除失败计数来制造成功。

合成管理员首次初始化遇到既有NAS风险服务直连故障；一次性限定CONNECT目标且不解密TLS的转发成功后自动关闭，随后登录/图片/重启验证没有转发。未使用生产凭据创建测试账号、未修改全局防火墙或其他应用。

最终仓库/架构/Alpha审计日志在同目录final-gates中补充；完整Alpha继续blocked、v0.1-start受控开发允许。未执行G4、Android真机、整机重启或实际回滚演练。

最终仓库门禁通过：verify_repository（14架构回归）、95/95 repository tests、Alpha有效性审计成功且发布仍blocked，v0.1-start仍允许。见same-nas-delivery/final-gates。
