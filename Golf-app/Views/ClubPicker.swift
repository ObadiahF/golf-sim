import SwiftUI

/// The bag: one big chip per club, the selected one in flag yellow.
struct ClubPicker: View {
    let selected: Int
    let onSelect: (Int) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                Text("Club").caption()
                Spacer()
                Text(Club.at(selected).name)
                    .font(.system(size: 15, weight: .semibold, design: .rounded))
                    .foregroundStyle(Theme.chalk)
            }
            HStack(spacing: 6) {
                ForEach(Club.bag.indices, id: \.self) { index in
                    chip(index)
                }
            }
        }
        .card(padding: 14)
    }

    private func chip(_ index: Int) -> some View {
        let isSelected = index == selected
        return Button { onSelect(index) } label: {
            Text(Club.bag[index].short)
                .font(.system(size: 18, weight: .heavy, design: .rounded))
                .frame(maxWidth: .infinity, minHeight: 54)
                .foregroundStyle(isSelected ? Theme.fairwayBottom : Theme.chalk)
                .background(isSelected ? Theme.flag : Color.white.opacity(0.08), in: .rect(cornerRadius: 14))
        }
        .buttonStyle(.plain)
        .accessibilityLabel(Club.bag[index].name)
        .accessibilityAddTraits(isSelected ? .isSelected : [])
    }
}
