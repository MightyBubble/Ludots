# 存档读档冷启动战报

## 场景

Bridge `ludots.save.write` → 销毁引擎（跨进程边界）→ `ludots.save.read` + `ludots.save.restore` → 续跑。

## 五时序节点

- 1_before_write tick=1 digest=454936C87481
- 2_after_write tick=2 path=/tmp/ludots-cold-start-4kfi3be1.e44/saves/manual/cold-start.ldsave digest=9B300C945483
- 3_restart storageRoot=/tmp/ludots-cold-start-4kfi3be1.e44 (engine disposed; new process boundary)
- 4_after_restore tick=2 digest=9B300C945483
- 5_after_continue tick=4 digest=2F644DC2C6E7

## 结论

- 落盘路径真实存在，跨引擎实例归一化 digest 一致。
- 续跑后 digest 变化，证明读档后世界可继续操作。
