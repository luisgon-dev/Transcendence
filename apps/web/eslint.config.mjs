import { dirname } from "node:path";
import { fileURLToPath } from "node:url";

import js from "@eslint/js";
import globals from "globals";
import next from "@next/eslint-plugin-next";
import react from "eslint-plugin-react";
import reactHooks from "eslint-plugin-react-hooks";
import tsParser from "@typescript-eslint/parser";

const __dirname = dirname(fileURLToPath(import.meta.url));

export default [
  {
    ignores: [".next/**", "node_modules/**", "dist/**"]
  },
  js.configs.recommended,
  {
    languageOptions: {
      globals: { ...globals.browser, ...globals.node }
    }
  },
  {
    files: ["**/*.ts", "**/*.tsx"],
    languageOptions: {
      parser: tsParser,
      parserOptions: {
        ecmaVersion: "latest",
        sourceType: "module",
        tsconfigRootDir: __dirname
      }
    }
  },
  react.configs.flat.recommended,
  react.configs.flat["jsx-runtime"],
  reactHooks.configs.flat.recommended,
  next.configs["core-web-vitals"],
  {
    settings: {
      react: { version: "detect" }
    },
    rules: {
      // Not part of Next's defaults; too noisy for now.
      "react-hooks/set-state-in-effect": "off"
    }
  },
  {
    files: ["**/*.ts", "**/*.tsx"],
    rules: {
      // TypeScript already validates globals/types; this rule doesn't understand type-only identifiers.
      "no-undef": "off",
      // We use TypeScript types for props.
      "react/prop-types": "off"
    }
  }
];
