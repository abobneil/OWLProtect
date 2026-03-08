export const theme = {
  fontFamily: "\"Roboto\", sans-serif",
  colors: {
    background: "#08070c",
    surface: "#14111b",
    surface2: "#1d1728",
    surface3: "#261d34",
    border: "#3d3350",
    text: "#f4f2f8",
    textMuted: "#aa9fbd",
    primary: "#5f33a3",
    primaryStrong: "#7b4bd0",
    good: "#22c55e",
    warn: "#facc15",
    bad: "#ef4444"
  }
} as const;

export type Theme = typeof theme;

