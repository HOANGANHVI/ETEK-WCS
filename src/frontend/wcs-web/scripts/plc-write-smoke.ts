// CR-016: when the PLC write action is available and why not (the cases of the acceptance table). Pure functions, no DOM. Run: npm run plcwrite:test
import { runtimeAllowsWrite, writeReadiness } from '../src/lib/plcWrite.ts'

const results: string[] = []
const check = (name: string, ok: boolean, info = '') => { results.push(`${ok ? 'PASS' : 'FAIL'} ${name}${ok || !info ? '' : ' — ' + info}`); if (!ok) process.exitCode = 1 }

const approved = { actualAddress: 'M101', writeApproval: 'APPROVED', writeSupported: true }
const pending = { ...approved, writeApproval: 'NOT_APPROVED' }
const readOnly = { effectiveMode: 'RealReadOnly', missingWriteKeys: ['PLC_WRITE_ENABLED', 'PLC_WRITE_APPROVAL_REFERENCE'] }
const readWrite = { effectiveMode: 'RealReadWrite', missingWriteKeys: [] }
const reason = (r: ReturnType<typeof writeReadiness>) => (r.ok ? 'OK' : r.reason)

check('ReadOnly + Approved: no write, and the missing keys are named', reason(writeReadiness(readOnly, 'Connected', approved)) === 'READ_ONLY'
  && JSON.stringify((writeReadiness(readOnly, 'Connected', approved) as { missing?: string[] }).missing) === JSON.stringify(readOnly.missingWriteKeys))
check('RealReadWrite + NotApproved: no write, asks for the approval', reason(writeReadiness(readWrite, 'Connected', pending)) === 'NOT_APPROVED')
check('RealReadWrite + Approved: the write is offered', reason(writeReadiness(readWrite, 'Connected', approved)) === 'OK')
check('a rejected point says so', reason(writeReadiness(readWrite, 'Connected', { ...approved, writeApproval: 'REJECTED' })) === 'REJECTED')
check('no address: no write', reason(writeReadiness(readWrite, 'Connected', { ...approved, actualAddress: null })) === 'NO_ADDRESS')
check('an unsupported type/encoding: no write', reason(writeReadiness(readWrite, 'Connected', { ...approved, writeSupported: false })) === 'UNSUPPORTED')
check('approved but the PLC is not connected: no write', reason(writeReadiness(readWrite, 'Reconnecting', approved)) === 'PLC_NOT_CONNECTED')
check('approved but the PLC state is unknown: no write', reason(writeReadiness(readWrite, undefined, approved)) === 'PLC_NOT_CONNECTED')
check('runtime not loaded yet: never assumed writable', reason(writeReadiness(null, 'Connected', approved)) === 'RUNTIME_UNKNOWN')
check('the runtime is judged before the point: read-only wins over a missing approval', reason(writeReadiness(readOnly, 'Connected', pending)) === 'READ_ONLY')
check('simulator writes to the simulator', reason(writeReadiness({ effectiveMode: 'Simulator' }, 'Connected', approved)) === 'OK')
check('only Simulator and RealReadWrite allow writing', runtimeAllowsWrite({ effectiveMode: 'Simulator' }) && runtimeAllowsWrite(readWrite) && !runtimeAllowsWrite(readOnly) && !runtimeAllowsWrite(null))
check('the approval alone is not enough in a read-only runtime (no hidden path)', ['APPROVED', 'NOT_APPROVED', 'REJECTED'].every((a) => reason(writeReadiness(readOnly, 'Connected', { ...approved, writeApproval: a })) === 'READ_ONLY'))

console.log(results.join('\n'))

// ---- CR-017: the checklist shows what the backend received, and agrees with the missing-key list the backend sends
import { writeKeyChecklist } from '../src/lib/plcWrite.ts'
const full = { mode: 'RealReadWrite', allowLiveConnection: true, writeEnabled: true, writeApprovalReferenceSet: true, writeHandshakeApproved: true }
const missingOf = (r: Parameters<typeof writeKeyChecklist>[0]) => writeKeyChecklist(r).filter((k) => !k.ok).map((k) => k.key)
check('every key received: nothing is reported missing', missingOf(full).length === 0)
check('a key the backend DID receive is not reported missing', JSON.stringify(missingOf({ ...full, writeEnabled: false })) === JSON.stringify(['PLC_WRITE_ENABLED']))
check('each key alone is named exactly', [
  ['allowLiveConnection', 'PLC_ALLOW_LIVE_CONNECTION'], ['writeEnabled', 'PLC_WRITE_ENABLED'], ['writeApprovalReferenceSet', 'PLC_WRITE_APPROVAL_REFERENCE'], ['writeHandshakeApproved', 'PLC_WRITE_HANDSHAKE_APPROVED'],
].every(([f, name]) => JSON.stringify(missingOf({ ...full, [f]: false })) === JSON.stringify([name])))
check('the requested mode RealReadOnly is shown as PLC_MODE with its current value', JSON.stringify(writeKeyChecklist({ ...full, mode: 'RealReadOnly' }).filter((k) => !k.ok)) === JSON.stringify([{ key: 'PLC_MODE', ok: false, current: 'RealReadOnly' }]))
check('an old backend (flags absent) is shown as missing, never as present', missingOf({ mode: 'RealReadWrite' }).length === 4)
// the same list the backend sends (WritePolicy.MissingWriteKeys) for the same flags
const backendList = ['PLC_ALLOW_LIVE_CONNECTION', 'PLC_WRITE_ENABLED', 'PLC_WRITE_APPROVAL_REFERENCE', 'PLC_WRITE_HANDSHAKE_APPROVED']
check('an empty deployment: the checklist equals the backend missingWriteKeys', JSON.stringify(missingOf({ mode: 'RealReadWrite', allowLiveConnection: false, writeEnabled: false, writeApprovalReferenceSet: false, writeHandshakeApproved: false })) === JSON.stringify(backendList))
console.log(results.slice(-6).join('\n'))
