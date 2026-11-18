import SwiftUI

struct ContentView: View {
    @StateObject private var viewModel = StreamViewModel()
    @State private var hostText: String = UserDefaults.standard.string(forKey: "lastHost") ?? ""

    var body: some View {
        ZStack {
            Color.black.ignoresSafeArea()

            if viewModel.isConnected {
                VideoDisplayViewRepresentable(view: viewModel.displayView)
                    .ignoresSafeArea()
                    .overlay(alignment: .topTrailing) {
                        Button {
                            viewModel.disconnect()
                        } label: {
                            Image(systemName: "xmark.circle.fill")
                                .font(.title)
                                .foregroundStyle(.white.opacity(0.7))
                                .padding()
                        }
                    }
            } else {
                connectForm
            }
        }
        .statusBarHidden(viewModel.isConnected)
    }

    private var connectForm: some View {
        VStack(spacing: 20) {
            Text("ExtendedScreen")
                .font(.largeTitle.bold())
                .foregroundStyle(.white)

            Text("Enter the Windows machine's LAN IP address (printed when you start the sender).")
                .font(.footnote)
                .foregroundStyle(.white.opacity(0.7))
                .multilineTextAlignment(.center)
                .padding(.horizontal, 40)

            TextField("e.g. 192.168.1.23", text: $hostText)
                .textFieldStyle(.roundedBorder)
                .keyboardType(.numbersAndPunctuation)
                .autocorrectionDisabled()
                .textInputAutocapitalization(.never)
                .frame(maxWidth: 320)

            Button("Connect") {
                let trimmed = hostText.trimmingCharacters(in: .whitespacesAndNewlines)
                UserDefaults.standard.set(trimmed, forKey: "lastHost")
                viewModel.connect(host: trimmed)
            }
            .buttonStyle(.borderedProminent)
            .disabled(hostText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)

            if let error = viewModel.lastError {
                Text(error)
                    .font(.footnote)
                    .foregroundStyle(.red)
                    .multilineTextAlignment(.center)
                    .padding(.horizontal, 40)
            }
        }
        .padding()
    }
}

#Preview {
    ContentView()
}
