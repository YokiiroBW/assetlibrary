# 无障碍生命周期对照记录

这些是调查中实际命令输出的整理，不伪称为保存的原始stdout文件。失败实现不参与当前生产/CTest依赖。

## 系统自动MSAA桥

Common Controls v6下分流：MSAA-only关闭可归还；UIA ElementFromHandle即可导致provider保留，FindAll不是必要条件；先释放client UIA对象再通过MSAA关闭仍保留。原始3轮30秒后 owners=[2,2,2]、MSAA providers=3，CoUninitialize才回全1/provider0。每次模型名称、选择和像素已清。

早期尝试的 WM_DESTROY map-clear 没有调用计数，后来发现活动canvas先清零可能跳过分支，故该早期“无效”不能单独作为结论。

已补做正确canvasIdentity控制：隔离源码副本 `.runtime/gallery-legacy-control-source/` 禁用自有UiaRootObjectId/native事件，仅使用系统MSAA桥；每轮实际打印 `legacy_control_codisconnect=0x00000000` 和 `legacy_control_own_hwnd_map_clear_executed=1`。3轮输出依次：

```text
after_case owners=[2,1,1] providers=1 nativeProviders=0 cleared=[1,0,0]
after_case owners=[2,2,1] providers=2 nativeProviders=0 cleared=[1,1,0]
after_case owners=[2,2,2] providers=3 nativeProviders=0 cleared=[1,1,1]
after_30s_max owners=[2,2,2] providers=3 nativeProviders=0 cleared=[1,1,1]
after_CoUninitialize owners=[1,1,1] providers=0 nativeProviders=0 cleared=[1,1,1]
```

源码 SHA-256 见 legacy-control-sources.json。结果说明正确清event map仍不足以回收该自动桥，不归因于未公开的COM私有实现。

## 手工系统wrapper

UiaProviderFromIAccessible factory S_OK、ProviderOptions=0x22、FragmentRoot/List接口存在，但实际外部FindAll ListItem返回0（stage100），不符合完整语义；精确断开返回0x80040201。root停止该方向；失败test-only源保存在本工作树 `.runtime/gallery-experiments/` 与隔离控制副本，不加入发布和当前CTest。

## 当前自有原生UIA

自有root/items逐对象退役与canvas WM_DESTROY map-clear组合解决回收；旧Query被presentation拒绝，独立Folder pin不再回指View。实际正常/调用关闭/换页和3轮通过；直接provider旧Selection返回UIA_E_ELEMENTNOTAVAILABLE，外部Windows客户端将其规范化为空数组S_OK，准确按“不泄露旧项”记录。未调用全局DisconnectAll、不额外Release、不强制卸载、不结束Explorer。

## 最后事件边界修正

统一选择通知后一次整组曾出现 retired_proxy 在2秒窗口后 MSAA1/native1/dispatcher1、pending0，而同组3轮通过；该失败保留，不能以随后一次通过抹去。Root审查识别 `UiaClientsAreListening` 是全局信号，不应为未被请求的画布主动Make root。7446e99改成只在本model已有当前root时发事件；所有真实输入仍经过通知helper，已有root下focus child按需取得。

修正后完整9/9先通过，再加受控listener及最终完整10/10。受控listener必须在任何MSAA取焦动作前注册并做“未取root”负控，否则监听者可以合法响应MSAA焦点而主动请求该root，不能把这种已经暴露的对象误称未观察。注册全局focus/选择、负控0provider、实收2次Selection、关闭全归零及3轮通过。该边界证据支持本次修复，未扩展为G4耐久证明。
