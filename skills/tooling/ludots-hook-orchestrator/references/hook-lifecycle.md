# Hook Lifecycle

Packet flow rules:

- producer writes a hook packet that conforms to `skills/contracts/hook.schema.json`
- orchestrator validates the packet and lists next consumer skills from `skills/registry.json`
- next agent consumes the packet and writes new artifacts or a new packet
- blocked states must be explicit and carry concrete evidence links
- timeout and retry budgets must be honored; exhausted budgets become blockers, not infinite polls

Do not implement invisible agent-to-agent calls. The hook packet is the only cross-agent contract.
