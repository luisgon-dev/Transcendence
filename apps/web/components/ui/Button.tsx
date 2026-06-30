"use client";

import * as React from "react";

import {
  buttonClassName,
  type ButtonSize,
  type ButtonVariant
} from "@/components/ui/buttonStyles";

export type ButtonProps = React.ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: ButtonVariant;
  size?: ButtonSize;
};

export const Button = React.forwardRef<HTMLButtonElement, ButtonProps>(
  ({ className, variant = "primary", size = "md", ...props }, ref) => (
    <button
      ref={ref}
      className={buttonClassName({ variant, size, className })}
      {...props}
    />
  )
);
Button.displayName = "Button";
