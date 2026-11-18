import Foundation
import Combine

@MainActor
final class StreamViewModel: ObservableObject, StreamClientDelegate {
    @Published var isConnected = false
    @Published var lastError: String?
    @Published var videoSize: (width: Int, height: Int)?

    let displayView = VideoDisplayUIView()

    private let client = StreamClient()
    private let decoder = H264StreamDecoder()

    init() {
        client.delegate = self
    }

    func connect(host: String) {
        lastError = nil
        client.connect(host: host, port: 7654)
    }

    func disconnect() {
        client.disconnect()
        isConnected = false
    }

    // MARK: - StreamClientDelegate (called on the client's background queue)

    nonisolated func streamClient(_ client: StreamClient, didReceiveVideoStreamInfo width: Int, height: Int, fps: Int) {
        Task { @MainActor in
            self.isConnected = true
            self.videoSize = (width, height)
        }
    }

    nonisolated func streamClient(_ client: StreamClient, didReceiveAccessUnit data: Data) {
        guard let sampleBuffer = decoder.makeSampleBuffer(fromAnnexB: data) else { return }
        Task { @MainActor in
            self.displayView.enqueue(sampleBuffer)
        }
    }

    nonisolated func streamClient(_ client: StreamClient, didDisconnectWithError error: Error?) {
        Task { @MainActor in
            self.isConnected = false
            if let error {
                self.lastError = error.localizedDescription
            }
        }
    }
}
