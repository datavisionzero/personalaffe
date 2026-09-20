import { createContext, useContext } from "react";
export type Privacy = { enabled: boolean; epoch: number; headers: Record<string, string>; toggle: () => void };
export const Context = createContext<Privacy>({ enabled: false, epoch: 0, headers: {}, toggle: () => undefined });
export const useBookmarkPrivacy = () => useContext(Context);
