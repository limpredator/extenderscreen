import Foundation
import Network

/// Implements the client side of docs/PROTOCOL.md: connects over TCP,
/// reads the one-time handshake, then keeps delivering VIDEO_FRAME payloads
/// (raw Annex-B access units) until the connection drops.
protocol StreamClientDelegate: AnyObject {
    func streamClient(_ client: StreamClient, didReceiveVideoStreamInfo width: Int, height: Int, fps: Int)
    func streamClient(_ client: StreamClient, didReceiveAccessUnit data: Data)
    func streamClient(_ client: StreamClient, didDisconnectWithError error: Error?)
}

enum StreamClientError: Error, LocalizedError {
    case badMagic
    var errorDescription: String? {
        switch self {
        case .badMagic: return "Unexpected data from server (bad handshake magic)."
        }
    }
}

final class StreamClient {
    weak var delegate: StreamClientDelegate?

    private var connection: NWConnection?
    private let queue = DispatchQueue(label: "com.extendedscreen.streamclient")

    private enum ParseState {
        case waitingForMagic
        case waitingForHandshakeRest
        case waitingForMessageHeader
        case waitingForPayload(type: UInt8, length: Int)
    }
    private var state: ParseState = .waitingForMagic
    private var buffer = Data()

    func connect(host: String, port: UInt16) {
        disconnect()

        let nwHost = NWEndpoint.Host(host)
        guard let nwPort = NWEndpoint.Port(rawValue: port) else { return }
        let connection = NWConnection(host: nwHost, port: nwPort, using: .tcp)
        self.connection = connection
        state = .waitingForMagic
        buffer.removeAll()

        connection.stateUpdateHandler = { [weak self] newState in
            guard let self else { return }
            switch newState {
            case .failed(let error):
                self.delegate?.streamClient(self, didDisconnectWithError: error)
            case .cancelled:
                self.delegate?.streamClient(self, didDisconnectWithError: nil)
            default:
                break
            }
        }
        connection.start(queue: queue)
        receiveLoop()
    }

    func disconnect() {
        connection?.cancel()
        connection = nil
    }

    private func receiveLoop() {
        connection?.receive(minimumIncompleteLength: 1, maximumLength: 64 * 1024) { [weak self] data, _, isComplete, error in
            guard let self else { return }

            if let data, !data.isEmpty {
                self.buffer.append(data)
                self.processBuffer()
            }

            if let error {
                self.delegate?.streamClient(self, didDisconnectWithError: error)
                return
            }
            if isComplete {
                self.delegate?.streamClient(self, didDisconnectWithError: nil)
                return
            }
            self.receiveLoop()
        }
    }

    private func processBuffer() {
        parseLoop: while true {
            switch state {
            case .waitingForMagic:
                guard buffer.count >= 4 else { return }
                guard buffer.prefix(4).elementsEqual("EXSC".utf8) else {
                    delegate?.streamClient(self, didDisconnectWithError: StreamClientError.badMagic)
                    return
                }
                buffer.removeFirst(4)
                state = .waitingForHandshakeRest

            case .waitingForHandshakeRest:
                // version(1) + width(4) + height(4) + fps(4)
                guard buffer.count >= 13 else { return }
                let width = readUInt32BE(buffer, at: buffer.startIndex + 1)
                let height = readUInt32BE(buffer, at: buffer.startIndex + 5)
                let fps = readUInt32BE(buffer, at: buffer.startIndex + 9)
                buffer.removeFirst(13)
                delegate?.streamClient(self, didReceiveVideoStreamInfo: Int(width), height: Int(height), fps: Int(fps))
                state = .waitingForMessageHeader

            case .waitingForMessageHeader:
                guard buffer.count >= 5 else { return }
                let type = buffer[buffer.startIndex]
                let length = Int(readUInt32BE(buffer, at: buffer.startIndex + 1))
                buffer.removeFirst(5)
                state = .waitingForPayload(type: type, length: length)

            case .waitingForPayload(let type, let length):
                guard buffer.count >= length else { return }
                let payload = buffer.prefix(length)
                buffer.removeFirst(length)
                if type == 0x01 {
                    delegate?.streamClient(self, didReceiveAccessUnit: Data(payload))
                } else if type == 0x04, payload.count >= 12 {
                    // STREAM_INFO (see docs/PROTOCOL.md): sent mid-stream
                    // when the source geometry changes on a live
                    // disconnect-free restart. VideoToolbox derives actual
                    // decode dimensions from each access unit's own SPS, so
                    // this isn't required for correct decoding here (unlike
                    // Android's MediaCodec) — forwarded anyway to keep
                    // StreamViewModel.videoSize accurate.
                    let width = readUInt32BE(payload, at: payload.startIndex)
                    let height = readUInt32BE(payload, at: payload.startIndex + 4)
                    let fps = readUInt32BE(payload, at: payload.startIndex + 8)
                    delegate?.streamClient(self, didReceiveVideoStreamInfo: Int(width), height: Int(height), fps: Int(fps))
                }
                state = .waitingForMessageHeader
            }
        }
    }

    private func readUInt32BE(_ data: Data, at offset: Int) -> UInt32 {
        (UInt32(data[offset]) << 24) | (UInt32(data[offset + 1]) << 16)
            | (UInt32(data[offset + 2]) << 8) | UInt32(data[offset + 3])
    }
}
