import { z } from "zod";

export const createUserSchema = z.object({
  username: z.string().trim().min(1, "Username is required").max(150),
  firstName: z.string().trim().min(1, "First name is required").max(150),
  lastName: z.string().trim().min(1, "Last name is required").max(150),
  password: z.string().min(1, "Password is required").max(200),
  email: z.string().trim().email("Email must be valid").optional().or(z.literal("")).transform((v) => v ?? "")
});

export type CreateUserForm = z.infer<typeof createUserSchema>;

export const editUserSchema = z.object({
  username: z.string().trim().min(1, "Username is required").max(150),
  firstName: z.string().trim().min(1, "First name is required").max(150),
  lastName: z.string().trim().min(1, "Last name is required").max(150),
  email: z.string().trim().email("Email must be valid").or(z.literal(""))
});

export type EditUserForm = z.infer<typeof editUserSchema>;

export const resetPasswordSchema = z.object({
  password: z.string().min(1, "Password is required").max(200)
});

export type ResetPasswordForm = z.infer<typeof resetPasswordSchema>;
