import path from "node:path";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      "@owlprotect/contracts": path.resolve(__dirname, "../../packages/contracts/src/index.ts"),
      "@owlprotect/theme": path.resolve(__dirname, "../../packages/theme/src/index.ts")
    }
  }
});
