using System.Runtime.CompilerServices;

// Cho phép project SubApp.ProtocolTests truy cập các lớp nội bộ (McFrameBuilder, McResponseParser)
// để kiểm thử đơn vị độc lập mà không cần mở rộng phạm vi công khai (public) ra ngoài assembly.
[assembly: InternalsVisibleTo("SubApp.ProtocolTests")]

