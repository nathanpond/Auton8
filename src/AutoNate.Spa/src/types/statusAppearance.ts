export type StatusAppearanceEntry = {
  id: string;
  status: string;
  color: string;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type CreateStatusAppearanceRequest = {
  status: string;
  color: string;
};

export type UpdateStatusAppearanceRequest = {
  status?: string;
  color?: string;
};
