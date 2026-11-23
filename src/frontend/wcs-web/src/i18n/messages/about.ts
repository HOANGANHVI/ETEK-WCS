import { defineMessages } from '../core'

export default defineMessages({
  web: 'Giao diện web: phiên bản {version}, commit {commit}, build {time}',
  api: 'Máy chủ (backend): phiên bản {version}, commit {commit}, build {time}',
  mismatch: 'Commit của giao diện web khác commit của backend: hệ thống có thể đang chạy lẫn hai bản build.',
  mismatchShort: 'backend khác commit',
}, {
  web: 'Web UI: version {version}, commit {commit}, built {time}',
  api: 'Server (backend): version {version}, commit {commit}, built {time}',
  mismatch: 'The web UI commit differs from the backend commit: the system may be running two different builds.',
  mismatchShort: 'backend commit differs',
})
